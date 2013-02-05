using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lorg
{
    public sealed class Logger
    {
        const double connectRetrySeconds = 10.0d;

        readonly ValidConfiguration cfg;

        /// <summary>
        /// Construct an instance with default configuration.
        /// </summary>
        public Logger(ValidConfiguration cfg)
        {
            this.cfg = cfg;
        }

        DateTime lastConnectAttempt = DateTime.UtcNow;
        bool noConnection = false;

        bool TryReconnect()
        {
            return DateTime.UtcNow.Subtract(lastConnectAttempt).TotalSeconds > connectRetrySeconds;
        }

        /// <summary>
        /// User-supplied configuration items.
        /// </summary>
        public sealed class Configuration
        {
            public string ApplicationName { get; set; }
            public string EnvironmentName { get; set; }
            public string ConnectionString { get; set; }

            /// <summary>
            /// Determines whether exception data is logged transactionally or not.
            /// </summary>
            public bool IsTransactional { get; set; }
        }

        /// <summary>
        /// Validated configuration items.
        /// </summary>
        public sealed class ValidConfiguration
        {
            public string ApplicationName { get; internal set; }
            public string EnvironmentName { get; internal set; }
            public string ConnectionString { get; internal set; }

            public string MachineName { get; internal set; }
            public string ProcessPath { get; internal set; }
            public string ApplicationIdentity { get; internal set; }
            public bool IsTransactional { get; internal set; }

            internal static readonly ValidConfiguration Default = new ValidConfiguration()
            {
                ApplicationName = String.Empty,
                EnvironmentName = String.Empty,
                ConnectionString = null,

                MachineName = Environment.MachineName,
                ProcessPath = Environment.GetCommandLineArgs()[0],
                ApplicationIdentity = String.Empty,
                IsTransactional = true
            };
        }

        /// <summary>
        /// Validates user-supplied configuration items.
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        public static ValidConfiguration ValidateConfiguration(Configuration cfg)
        {
            // Validate required fields:
            if (String.IsNullOrWhiteSpace(cfg.ApplicationName)) throw new ArgumentNullException("ApplicationName");
            if (String.IsNullOrWhiteSpace(cfg.EnvironmentName)) throw new ArgumentNullException("EnvironmentName");

            // Ensure that AsynchronousProcessing and MultipleActiveResultSets are both enabled:
            var csb = new SqlConnectionStringBuilder(cfg.ConnectionString);
            csb.AsynchronousProcessing = true;
            // TODO(jsd): Consider not requiring MARS and use multiple connections instead?
            csb.MultipleActiveResultSets = true;

            // Return the ValidConfiguration type that asserts we validated the user config:
            return new ValidConfiguration
            {
                ApplicationName = cfg.ApplicationName,
                EnvironmentName = cfg.EnvironmentName,
                ConnectionString = csb.ToString(),

                MachineName = Environment.MachineName,
                ProcessPath = Environment.GetCommandLineArgs()[0],
                ApplicationIdentity = Thread.CurrentPrincipal.Identity.Name,
                IsTransactional = cfg.IsTransactional
            };
        }

        /// <summary>
        /// Write the exception to the database.
        /// </summary>
        /// <returns></returns>
        public async Task<ILogIdentifier> Write(ExceptionWithCapturedContext thrown)
        {
            try
            {
                return await WriteDatabase(thrown);
            }
            catch (Exception ourEx)
            {
                var actual = new ExceptionWithCapturedContext(ourEx, isHandled: true);
                var report = new LoggerExceptionWithCapturedContext(actual, thrown);
                FailoverWrite(report);
                return (ILogIdentifier)null;
            }
        }

        WebHostingContext CurrentWebHost = new WebHostingContext();

        /// <summary>
        /// Wrapper method to catch and log exceptions thrown by <paramref name="a"/>.
        /// </summary>
        /// <param name="a">Asynchronous task to catch exceptions from.</param>
        /// <param name="isHandled">Whether the exception is explicitly handled or not.</param>
        /// <param name="correlationID">A unique identifier to tie two or more exception reports together.</param>
        /// <returns></returns>
        public async Task HandleExceptions(Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            ExceptionWithCapturedContext exToLog = null;

            try
            {
                await a();
            }
            catch (Exception ex)
            {
                // NOTE(jsd): `await` is not allowed in catch blocks.
                exToLog = new ExceptionWithCapturedContext(
                    ex,
                    isHandled,
                    correlationID,
                    CurrentWebHost,
                    new CapturedHttpContext(System.Web.HttpContext.Current)
                );
            }

            if (exToLog != null)
            {
                await Write(exToLog);
            }
        }

        /// <summary>
        /// The failover case for when nothing seems to be set up as expected.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        public void FailoverWrite(ExceptionWithCapturedContext ctx)
        {
            var sb = new StringBuilder();
            Format(ctx, sb, 0);
            string output = sb.ToString();

            FailoverWriteString(output);
        }

        internal void FailoverWrite(LoggerExceptionWithCapturedContext ctx)
        {
            // Format:
            var sb = new StringBuilder();
            sb.Append("Actual:");
            sb.Append(nl);
            sb.Append(IndentString(1));
            Format(ctx.Actual, sb, 1);
            sb.Append(nl);
            sb.Append(IndentString(1));
            sb.Append("Symptom:");
            Format(ctx.Symptom, sb, 1);
            string output = sb.ToString();

            FailoverWriteString(output);
        }

        void FailoverWriteString(string output)
        {
            // Write to the Trace:
            Trace.WriteLine(output);
            try
            {
                // Log a Windows event log entry:
                EventLog.WriteEntry(cfg.ApplicationName, output, EventLogEntryType.Error);
            }
            catch
            {
                // I got nothin'.
            }
        }

        public static string FormatException(ExceptionWithCapturedContext ctx)
        {
            if (ctx == null) return "(null)";

            var sb = new StringBuilder();
            Format(ctx, sb, 0);
            return sb.ToString();
        }

        #region Implementation details

        class ExceptionPolicy
        {
            /// <summary>
            /// Determines whether or not web context details are logged.
            /// </summary>
            public bool LogWebContext { get; private set; }
            /// <summary>
            /// Determines whether or not web context details are logged.
            /// </summary>
            public bool LogWebRequestHeaders { get; private set; }

            public ExceptionPolicy(bool logWebContext, bool logWebRequestHeaders)
            {
                LogWebContext = logWebContext;
                LogWebRequestHeaders = logWebRequestHeaders;
            }

            /// <summary>
            /// Default exception-handling policy.
            /// </summary>
            public static ExceptionPolicy Default = new ExceptionPolicy(
                logWebContext: true,
                logWebRequestHeaders: true
            );
        }

        /// <summary>
        /// Record the exception to the various database tables.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        async Task<ILogIdentifier> WriteDatabase(ExceptionWithCapturedContext ctx, int? parentInstanceID = null)
        {
            using (var conn = new SqlConnection(cfg.ConnectionString))
            {
                bool attempt = true;
                if (noConnection) attempt = TryReconnect();
                if (!attempt) return null;

                try
                {
                    // Open connection and check connectivity:
                    await conn.OpenAsync();
                }
                catch (Exception inex)
                {
                    // No connectivity; reset attempt timer:
                    noConnection = true;
                    lastConnectAttempt = DateTime.UtcNow;

                    FailoverWrite(new ExceptionWithCapturedContext(inex, isHandled: true));
                    return null;
                }

                if (cfg.IsTransactional)
                {
                    // Transactional logging:
                    using (var tran = conn.BeginTransaction(IsolationLevel.Snapshot))
                    {
                        var connCtx = new SqlConnectionContext(conn, tran);
                        try
                        {
                            var logged = await LogExceptionRecursively(connCtx, ctx, null);
                            tran.Commit();
                            return logged;
                        }
                        catch (Exception ex)
                        {
                            tran.Rollback();
                            FailoverWrite(new ExceptionWithCapturedContext(ex, isHandled: true));
                            return null;
                        }
                    }
                }
                else
                {
                    // Non-transactional logging:
                    var connCtx = new SqlConnectionContext(conn);
                    var logged = await LogExceptionRecursively(connCtx, ctx, null);
                    return logged;
                }
            }
        }

        async Task<HashedLogIdentifier> LogExceptionRecursively(SqlConnectionContext conn, ExceptionWithCapturedContext ctx, int? parentInstanceID = null)
        {
            // Create the exTargetSite if it does not exist:
            var ts = ctx.TargetSite;
            byte[] exTargetSiteID = null;
            Task tskTargetSite = null;
            if (ts != null)
            {
                exTargetSiteID = ts.TargetSiteID;

                tskTargetSite = conn.ExecNonQuery(
@"MERGE [dbo].[exTargetSite] WITH (HOLDLOCK) AS target
USING (SELECT @exTargetSiteID) AS source (exTargetSiteID)
ON (target.exTargetSiteID = source.exTargetSiteID)
WHEN NOT MATCHED THEN
    INSERT ([exTargetSiteID], [AssemblyName], [TypeName], [MethodName], [ILOffset], [FileName], [FileLineNumber], [FileColumnNumber])
    VALUES (@exTargetSiteID,  @AssemblyName,  @TypeName , @MethodName , @ILOffset , @FileName , @FileLineNumber , @FileColumnNumber );",
                    prms =>
                        prms.AddInParamSHA1("@exTargetSiteID", exTargetSiteID)
                            .AddInParamSize("@AssemblyName", SqlDbType.NVarChar, 256, ts.AssemblyName)
                            .AddInParamSize("@TypeName", SqlDbType.NVarChar, 256, ts.TypeName)
                            .AddInParamSize("@MethodName", SqlDbType.NVarChar, 256, ts.MethodName)
                            .AddInParam("@ILOffset", SqlDbType.Int, ts.ILOffset)
                            .AddInParamSize("@FileName", SqlDbType.NVarChar, 256, ts.FileName)
                            .AddInParam("@FileLineNumber", SqlDbType.Int, ts.FileLineNumber)
                            .AddInParam("@FileColumnNumber", SqlDbType.Int, ts.FileColumnNumber)
                );
            }

            // Create the exException record if it does not exist:
            var tskGetPolicy = conn.ExecReader(
@"MERGE [dbo].[exException] WITH (HOLDLOCK) AS target
USING (SELECT @exExceptionID) AS source (exExceptionID)
ON (target.exExceptionID = source.exExceptionID)
WHEN NOT MATCHED THEN
    INSERT ([exExceptionID], [AssemblyName], [TypeName], [StackTrace], [exTargetSiteID])
    VALUES (@exExceptionID,  @AssemblyName,  @TypeName,  @StackTrace,  @exTargetSiteID );

SELECT excpol.[LogWebContext], excpol.[LogWebRequestHeaders]
FROM [dbo].[exExceptionPolicy] excpol WITH (NOLOCK)
WHERE excpol.[exExceptionID] = @exExceptionID;",
                prms =>
                    prms.AddInParamSHA1("@exExceptionID", ctx.ExceptionID)
                        .AddInParamSize("@AssemblyName", SqlDbType.NVarChar, 256, ctx.AssemblyName)
                        .AddInParamSize("@TypeName", SqlDbType.NVarChar, 256, ctx.TypeName)
                        .AddInParamSize("@StackTrace", SqlDbType.NVarChar, -1, ctx.StackTrace)
                        .AddInParamSHA1("@exTargetSiteID", exTargetSiteID),
                // Read the SELECT result set into an ExceptionPolicy, or use the default policy:
                async dr =>
                    !await dr.ReadAsync()
                    ? ExceptionPolicy.Default
                    : new ExceptionPolicy(
                        logWebContext: dr.GetBoolean(dr.GetOrdinal("LogWebContext")),
                        logWebRequestHeaders: dr.GetBoolean(dr.GetOrdinal("LogWebRequestHeaders"))
                    )
            );

            // Create the exException record if it does not exist:
            byte[] exApplicationID = CalcApplicationID(cfg);
            var tskApplication = conn.ExecNonQuery(
@"MERGE [dbo].[exApplication] WITH (HOLDLOCK) AS target
USING (SELECT @exApplicationID) AS source (exApplicationID)
ON (target.exApplicationID = source.exApplicationID)
WHEN NOT MATCHED THEN
    INSERT ([exApplicationID], [MachineName], [ApplicationName], [EnvironmentName], [ProcessPath])
    VALUES (@exApplicationID,  @MachineName,  @ApplicationName,  @EnvironmentName,  @ProcessPath );",
                prms =>
                    prms.AddInParamSHA1("@exApplicationID", exApplicationID)
                        .AddInParamSize("@MachineName", SqlDbType.VarChar, 64, cfg.MachineName)
                        .AddInParamSize("@ApplicationName", SqlDbType.VarChar, 96, cfg.ApplicationName)
                        .AddInParamSize("@EnvironmentName", SqlDbType.VarChar, 32, cfg.EnvironmentName)
                        .AddInParamSize("@ProcessPath", SqlDbType.NVarChar, 256, cfg.ProcessPath)
            );

            // Create the instance record:
            var tskInstance = conn.ExecNonQuery(
@"INSERT INTO [dbo].[exInstance]
       ([exExceptionID], [exApplicationID], [LoggedTimeUTC], [SequenceNumber], [IsHandled], [ApplicationIdentity], [ParentInstanceID], [CorrelationID], [ManagedThreadId], [Message])
VALUES (@exExceptionID,  @exApplicationID,  @LoggedTimeUTC,  @SequenceNumber,  @IsHandled,  @ApplicationIdentity,  @ParentInstanceID,  @CorrelationID,  @ManagedThreadId,  @Message );
SET @exInstanceID = SCOPE_IDENTITY();",
                prms =>
                    prms.AddOutParam("@exInstanceID", SqlDbType.Int)
                        .AddInParamSHA1("@exExceptionID", ctx.ExceptionID)
                        .AddInParamSHA1("@exApplicationID", exApplicationID)
                        .AddInParam("@LoggedTimeUTC", SqlDbType.DateTime2, ctx.LoggedTimeUTC)
                        .AddInParam("@SequenceNumber", SqlDbType.Int, ctx.SequenceNumber)
                        .AddInParam("@IsHandled", SqlDbType.Bit, ctx.IsHandled)
                        .AddInParamSize("@ApplicationIdentity", SqlDbType.NVarChar, 128, cfg.ApplicationIdentity)
                        .AddInParam("@ParentInstanceID", SqlDbType.Int, parentInstanceID)
                        .AddInParam("@CorrelationID", SqlDbType.UniqueIdentifier, ctx.CorrelationID)
                        .AddInParam("@ManagedThreadId", SqlDbType.Int, ctx.ManagedThreadID)
                        .AddInParamSize("@Message", SqlDbType.NVarChar, 256, ctx.Exception.Message),
                (prms, rc) =>
                {
                    return (int)prms["@exInstanceID"].Value;
                }
            );

            // Await the exInstance record creation:
            int exInstanceID = await tskInstance;

            // Await the exception policy result:
            var policy = await tskGetPolicy;

            // If logging the web context is enabled and we have a web context to work with, log it:
            Task tskLoggingWeb = null;
            if (policy.LogWebContext && ctx.CapturedHttpContext != null)
            {
                tskLoggingWeb = LogWebContext(conn, policy, ctx, exInstanceID);
            }

            // Wait for any outstanding logging tasks:
            if (tskLoggingWeb != null) await tskLoggingWeb;
            if (tskTargetSite != null) await tskTargetSite;
            await tskApplication;

            // Recursively log inner exceptions:
            ExceptionWithCapturedContext inner = ctx.InnerException;
            parentInstanceID = exInstanceID;
            while (inner != null)
            {
                parentInstanceID = (await LogExceptionRecursively(conn, inner, parentInstanceID)).InstanceID;
                inner = inner.InnerException;
            }

            return new HashedLogIdentifier(ctx.ExceptionID, exInstanceID);
        }

        async Task LogWebContext(SqlConnectionContext conn, ExceptionPolicy policy, ExceptionWithCapturedContext ctx, int exInstanceID)
        {
            var http = ctx.CapturedHttpContext;
            var host = ctx.WebHostingContext;

            // We require both HTTP and Host context:
            if (http == null || host == null)
                return;

            // Try to get the authenticated user for the HTTP context:
            string authUserName = null;
            if (http.User != null && http.User.Identity != null)
                authUserName = http.User.Identity.Name;

            // Compute the IDs:
            byte[] requestURLQueryID = CalcURLQueryID(http.Url);
            byte[] referrerURLQueryID = http.UrlReferrer == null ? null : CalcURLQueryID(http.UrlReferrer);
            byte[] exWebApplicationID = CalcWebApplicationID(host);

            // Log the web application details:
            var tskWebApplication = conn.ExecNonQuery(
@"MERGE [dbo].[exWebApplication] WITH (HOLDLOCK) AS target
USING (SELECT @exWebApplicationID) AS source (exWebApplicationID)
ON (target.exWebApplicationID = source.exWebApplicationID)
WHEN NOT MATCHED THEN
    INSERT ([exWebApplicationID], [MachineName], [ApplicationID], [PhysicalPath], [VirtualPath], [SiteName])
    VALUES (@exWebApplicationID,  @MachineName,  @ApplicationID,  @PhysicalPath,  @VirtualPath,  @SiteName );",
                prms =>
                    prms.AddInParamSHA1("@exWebApplicationID", exWebApplicationID)
                        .AddInParamSize("@MachineName", SqlDbType.NVarChar, 96, host.MachineName)
                        .AddInParamSize("@ApplicationID", SqlDbType.VarChar, 96, host.ApplicationID)
                        .AddInParamSize("@PhysicalPath", SqlDbType.NVarChar, 256, host.PhysicalPath)
                        .AddInParamSize("@VirtualPath", SqlDbType.NVarChar, 256, host.VirtualPath)
                        .AddInParamSize("@SiteName", SqlDbType.VarChar, 96, host.SiteName)
            );

            // Log the request headers collection, if requested and available:
            Task tskCollection = null;
            byte[] exCollectionID = null;
            if (policy.LogWebRequestHeaders && http.Headers != null)
            {
                // Compute the collection hash (must be done BEFORE `tskContextWeb`):
                exCollectionID = CalcCollectionID(http.Headers);
                // Store all records for the headers collection:
                tskCollection = LogCollection(conn, exCollectionID, http.Headers);
            }

            // Log the web context:
            var tskContextWeb = conn.ExecNonQuery(
@"INSERT INTO [dbo].[exContextWeb]
       ([exInstanceID], [exWebApplicationID], [AuthenticatedUserName], [HttpVerb], [RequestURLQueryID], [ReferrerURLQueryID], [RequestHeadersCollectionID])
VALUES (@exInstanceID,  @exWebApplicationID,  @AuthenticatedUserName,  @HttpVerb,  @RequestURLQueryID,  @ReferrerURLQueryID,  @RequestHeadersCollectionID );",
                prms =>
                    prms.AddInParam("@exInstanceID", SqlDbType.Int, exInstanceID)
                    // Hosting environment:
                        .AddInParamSHA1("@exWebApplicationID", exWebApplicationID)
                    // Request details:
                        .AddInParamSize("@AuthenticatedUserName", SqlDbType.VarChar, 96, authUserName)
                        .AddInParamSize("@HttpVerb", SqlDbType.VarChar, 16, http.HttpMethod)
                        .AddInParamSHA1("@RequestURLQueryID", requestURLQueryID)
                        .AddInParamSHA1("@ReferrerURLQueryID", referrerURLQueryID)
                        .AddInParamSHA1("@RequestHeadersCollectionID", exCollectionID)
            );

            // Log the URLs:
            Task tskRequestURL, tskReferrerURL;
            tskRequestURL = LogURLQuery(conn, http.Url);
            if (http.UrlReferrer != null)
                tskReferrerURL = LogURLQuery(conn, http.UrlReferrer);
            else
                tskReferrerURL = null;

            // Await the completion of the tasks:
            await Task.WhenAll(tskRequestURL, tskWebApplication, tskContextWeb);
            if (tskReferrerURL != null) await tskReferrerURL;
            if (tskCollection != null) await tskCollection;
        }

        /// <summary>
        /// Writes a URL without query-string to the exURL table.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        Task LogURL(SqlConnectionContext conn, Uri uri)
        {
            byte[] urlID = CalcURLID(uri);

            return conn.ExecNonQuery(
@"MERGE [dbo].[exURL] WITH (HOLDLOCK) AS target
USING (SELECT @exURLID) AS source (exURLID)
ON (target.exURLID = source.exURLID)
WHEN NOT MATCHED THEN
   INSERT ([exURLID], [HostName], [PortNumber], [AbsolutePath], [Scheme])
   VALUES (@exURLID,  @HostName,  @PortNumber,  @AbsolutePath,  @Scheme );",
                prms =>
                    prms.AddInParamSHA1("@exURLID", urlID)
                        .AddInParamSize("@HostName", SqlDbType.VarChar, 128, uri.Host)
                        .AddInParam("@PortNumber", SqlDbType.Int, (int)uri.Port)
                        .AddInParamSize("@AbsolutePath", SqlDbType.VarChar, 512, uri.AbsolutePath)
                        .AddInParamSize("@Scheme", SqlDbType.VarChar, 8, uri.Scheme)
            );
        }

        /// <summary>
        /// Writes a URL with query-string to the exURLQuery table.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        async Task LogURLQuery(SqlConnectionContext conn, Uri uri)
        {
            // Log the base URL:
            byte[] urlID = CalcURLID(uri);

            // Compute the URLQueryID:
            byte[] urlQueryID = CalcURLQueryID(uri);

            // Store the exURL record:
            var tskLogURL = LogURL(conn, uri);

            // Store the exURLQuery record:
            var tskLogURLQuery = conn.ExecNonQuery(
@"MERGE [dbo].[exURLQuery] WITH (HOLDLOCK) AS target
USING (SELECT @exURLQueryID) AS source (exURLQueryID)
ON (target.exURLQueryID = source.exURLQueryID)
WHEN NOT MATCHED THEN
    INSERT ([exURLQueryID], [exURLID], [QueryString])
    VALUES (@exURLQueryID,  @exURLID,  @QueryString);",
                prms =>
                    prms.AddInParamSHA1("@exURLQueryID", urlQueryID)
                        .AddInParamSHA1("@exURLID", urlID)
                        .AddInParamSize("@QueryString", SqlDbType.VarChar, -1, uri.Query)
            );

            await Task.WhenAll(tskLogURLQuery, tskLogURL);
        }

        /// <summary>
        /// Logs all name/value pairs as a single collection using concurrent MERGE statements.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="exCollectionID"></param>
        /// <param name="coll"></param>
        /// <returns></returns>
        async Task LogCollection(SqlConnectionContext conn, byte[] exCollectionID, NameValueCollection coll)
        {
            // The exCollectionID should be pre-calculated by `CalcCollectionID`.

            // Check if the exCollectionID exists already:
            int? collectionCount = await conn.ExecReader(
@"SELECT COUNT(exCollectionID) FROM [dbo].[exCollectionKeyValue] WHERE [exCollectionID] = @exCollectionID",
                prms =>
                    prms.AddInParamSHA1("@exCollectionID", exCollectionID),
                    async dr => await dr.ReadAsync() ? dr.GetInt32(0) : (int?)null
            );

            // Don't bother logging name-value pairs if the collection already exists:
            if (!collectionCount.HasValue) return;
            if (collectionCount.Value == coll.Count) return;

            const int numTasksPerPair = 2;

            // Create an array of tasks to wait upon:
            var tasks = new Task[coll.Count * numTasksPerPair];

            // Fill out the array of tasks with concurrent MERGE statements for each name/value pair:
            for (int i = 0; i < coll.Count; ++i)
            {
                string name = coll.GetKey(i);
                string value = coll.Get(i);

                byte[] exCollectionValueID = Hash.SHA1(value);

                // Merge the Value record:
                tasks[i * numTasksPerPair + 0] = conn.ExecNonQuery(
@"MERGE [dbo].[exCollectionValue] WITH (HOLDLOCK) AS target
USING (SELECT @exCollectionValueID) AS source (exCollectionValueID)
ON (target.exCollectionValueID = source.exCollectionValueID)
WHEN NOT MATCHED THEN
    INSERT ([exCollectionValueID], [Value])
    VALUES (@exCollectionValueID,  @Value );",
                    prms =>
                        prms.AddInParamSHA1("@exCollectionValueID", exCollectionValueID)
                            .AddInParamSize("@Value", SqlDbType.VarChar, -1, value)
                );

                // Merge the Name-Value record:
                tasks[i * numTasksPerPair + 1] = conn.ExecNonQuery(
@"MERGE [dbo].[exCollectionKeyValue] WITH (HOLDLOCK) AS target
USING (SELECT @exCollectionID, @Name, @exCollectionValueID) AS source (exCollectionID, Name, exCollectionValueID)
ON (target.exCollectionID = source.exCollectionID AND target.Name = source.Name AND target.exCollectionValueID = source.exCollectionValueID)
WHEN NOT MATCHED THEN
    INSERT ([exCollectionID], [Name], [exCollectionValueID])
    VALUES (@exCollectionID,  @Name,  @exCollectionValueID );",
                    prms =>
                        prms.AddInParamSHA1("@exCollectionID", exCollectionID)
                            .AddInParamSize("@Name", SqlDbType.VarChar, 96, name)
                            .AddInParamSHA1("@exCollectionValueID", exCollectionValueID)
                );
            }

            // Our final task's completion depends on all the tasks created thus far:
            await Task.WhenAll(tasks);
        }

        static byte[] CalcCollectionID(NameValueCollection coll)
        {
            // Guesstimate the capacity required (40 chars per name/value pair):
            var sb = new StringBuilder(coll.Count * 40);

            // Compute the hash of the collection as a series of name-ordered "{name}:{value}\n" strings:
            foreach (string name in coll.AllKeys.OrderBy(k => k))
            {
                sb.AppendFormat("{0}:{1}\n", name, coll.Get(name));
            }

            byte[] id = Hash.SHA1(sb.ToString());
            return id;
        }

        static byte[] CalcApplicationID(ValidConfiguration cfg)
        {
            byte[] id = Hash.SHA1(
                String.Concat(
                    cfg.MachineName, ":",
                    cfg.ApplicationName, ":",
                    cfg.EnvironmentName, ":",
                    cfg.ProcessPath
                )
            );
            return id;
        }

        static byte[] CalcWebApplicationID(WebHostingContext host)
        {
            byte[] id = Hash.SHA1(
                String.Concat(
                    host.MachineName, ":",
                    host.ApplicationID, ":",
                    host.PhysicalPath, ":",
                    host.VirtualPath, ":",
                    host.SiteName
                )
            );
            return id;
        }

        static byte[] CalcURLID(Uri uri)
        {
            byte[] id = Hash.SHA1("{0}://{1}:{2}{3}".F(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath));
            Debug.Assert(id.Length == 20);
            return id;
        }

        static byte[] CalcURLQueryID(Uri uri)
        {
            byte[] id = Hash.SHA1("{0}://{1}:{2}{3}{4}".F(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri.Query));
            Debug.Assert(id.Length == 20);
            return id;
        }

        const string nl = "\n";

        static string IndentString(int indent)
        {
            return new string(' ', indent * 2);
        }

        static void Format(ExceptionWithCapturedContext ctx, StringBuilder sb, int indent)
        {
            if (ctx == null) return;

            string indentStr = IndentString(indent);
            string nlIndent = nl + indentStr;
            sb.Append(indentStr);

            string tmp = ctx.Exception.ToString().Replace("\r\n", nl).Replace(nl, nlIndent);
            sb.Append(tmp);

            if (ctx.InnerException != null)
            {
                sb.Append(nlIndent);
                sb.Append("Inner:");
                sb.Append(nl);
                Format(ctx.InnerException, sb, indent + 1);
            }
        }

        #endregion
    }
}
