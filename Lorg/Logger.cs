using System;
using System.Collections.Generic;
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
        const double connectRetrySeconds = 30.0d;

        readonly ValidConfiguration cfg;

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

            internal static readonly ValidConfiguration Default = new ValidConfiguration()
            {
                ApplicationName = String.Empty,
                EnvironmentName = String.Empty,
                ConnectionString = null,

                MachineName = Environment.MachineName,
                ProcessPath = Environment.GetCommandLineArgs()[0],
                ApplicationIdentity = String.Empty
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
                ApplicationIdentity = Thread.CurrentPrincipal.Identity.Name
            };
        }

        /// <summary>
        /// Construct an instance with default configuration.
        /// </summary>
        public Logger(ValidConfiguration cfg)
        {
            this.cfg = cfg;
        }

        /// <summary>
        /// Write the exception to the database.
        /// </summary>
        /// <returns></returns>
        public async Task Write(ExceptionWithCapturedContext thrown)
        {
            try
            {
                await WriteDatabase(thrown);
            }
            catch (Exception ourEx)
            {
                var actual = new ExceptionWithCapturedContext(ourEx, isHandled: true);
                var report = new LoggerExceptionWithCapturedContext(actual, thrown);
                FailoverWrite(report);
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
            Exception exToLog = null;

            try
            {
                await a();
            }
            catch (Exception ex)
            {
                // NOTE(jsd): `await` is not allowed in catch blocks.
                exToLog = ex;
            }

            if (exToLog != null)
            {
                await Write(new ExceptionWithCapturedContext(
                    exToLog,
                    isHandled,
                    correlationID,
                    CurrentWebHost,
                    new CapturedHttpContext(System.Web.HttpContext.Current)
                ));
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
            /// Determines whether or not stack frames are logged in detail.
            /// </summary>
            public bool LogStackContext { get; private set; }
            /// <summary>
            /// Determines whether or not web context details are logged.
            /// </summary>
            public bool LogWebContext { get; private set; }

            public ExceptionPolicy(bool logStackContext, bool logWebContext)
            {
                LogStackContext = logStackContext;
                LogWebContext = logWebContext;
            }

            /// <summary>
            /// Default exception-handling policy.
            /// </summary>
            public static ExceptionPolicy Default = new ExceptionPolicy(
                logStackContext: false,
                logWebContext: true
            );
        }

        /// <summary>
        /// Record the exception to the various database tables.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        async Task<Tuple<byte[], int>> WriteDatabase(ExceptionWithCapturedContext ctx)
        {
            using (var conn = new SqlConnection(cfg.ConnectionString))
            {
                bool attempt = true;
                if (noConnection) attempt = TryReconnect();
                if (!attempt) return null;

                try
                {
                    await conn.OpenAsync();
                }
                catch (SqlException sqex)
                {
                    noConnection = true;
                    lastConnectAttempt = DateTime.UtcNow;

                    FailoverWrite(new ExceptionWithCapturedContext(sqex, isHandled: true));
                }

                // Create the exException record if it does not exist:
                var tskGetPolicy = ExecReader(
                    conn,
@"MERGE [dbo].[exException] AS target
USING (SELECT @exExceptionID) AS source (exExceptionID)
ON (target.exExceptionID = source.exExceptionID)
WHEN NOT MATCHED THEN
    INSERT ([exExceptionID], [AssemblyName], [TypeName], [StackTrace])
    VALUES (@exExceptionID, @assemblyName, @typeName, @stackTrace);

SELECT [LogStackContext], [LogWebContext]
FROM [dbo].[exExceptionPolicy] WITH (NOLOCK)
WHERE exExceptionID = @exExceptionID;",
                    prms =>
                    {
                        AddParameterWithSize(prms, "@exExceptionID", SqlDbType.Binary, 20, ctx.ExceptionID);
                        AddParameterWithSize(prms, "@assemblyName", SqlDbType.NVarChar, 256, ctx.AssemblyName);
                        AddParameterWithSize(prms, "@typeName", SqlDbType.NVarChar, 256, ctx.TypeName);
                        AddParameterWithSize(prms, "@stackTrace", SqlDbType.NVarChar, -1, ctx.StackTrace);
                    },
                    // Read the SELECT result set into an ExceptionPolicy, or use the default policy:
                    async dr =>
                        !await dr.ReadAsync()
                        ? ExceptionPolicy.Default
                        : new ExceptionPolicy(
                            logStackContext: dr.GetBoolean(dr.GetOrdinal("LogStackContext")),
                            logWebContext: dr.GetBoolean(dr.GetOrdinal("LogWebContext"))
                        )
                );

                // Create the instance record:
                int exInstanceID = await ExecNonQuery(
                    conn,
@"INSERT INTO [dbo].[exInstance] ([exExceptionID], [LoggedTimeUTC], [IsHandled], [CorrelationID], [Message], [ApplicationName], [EnvironmentName], [MachineName], [ProcessPath], [ExecutingAssemblyName], [ManagedThreadId], [ApplicationIdentity])
VALUES (@exExceptionID, @loggedTimeUTC, @isHandled, @correlationID, @message, @applicationName, @environmentName, @machineName, @processPath, @executingAssemblyName, @managedThreadId, @applicationIdentity);
SET @exInstanceID = SCOPE_IDENTITY();",
                    prms =>
                    {
                        AddParameterOut(prms, "@exInstanceID", SqlDbType.Int);
                        AddParameterWithSize(prms, "@exExceptionID", SqlDbType.Binary, 20, ctx.ExceptionID);
                        AddParameter(prms, "@loggedTimeUTC", SqlDbType.DateTime2, ctx.LoggedTimeUTC);
                        AddParameter(prms, "@isHandled", SqlDbType.Bit, ctx.IsHandled);
                        AddParameter(prms, "@correlationID", SqlDbType.UniqueIdentifier, AsDBNull(ctx.CorrelationID));
                        AddParameterWithSize(prms, "@message", SqlDbType.NVarChar, 256, ctx.Exception.Message);
                        AddParameterWithSize(prms, "@applicationName", SqlDbType.VarChar, 96, cfg.ApplicationName);
                        AddParameterWithSize(prms, "@environmentName", SqlDbType.VarChar, 96, cfg.EnvironmentName);
                        AddParameterWithSize(prms, "@machineName", SqlDbType.VarChar, 64, cfg.MachineName);
                        AddParameterWithSize(prms, "@processPath", SqlDbType.NVarChar, 256, cfg.ProcessPath);
                        AddParameterWithSize(prms, "@executingAssemblyName", SqlDbType.NVarChar, 256, ctx.Exception.TargetSite.DeclaringType.Assembly.FullName);
                        AddParameter(prms, "@managedThreadId", SqlDbType.Int, ctx.ManagedThreadID);
                        AddParameterWithSize(prms, "@applicationIdentity", SqlDbType.NVarChar, 128, cfg.ApplicationIdentity);
                    },
                    (prms, rc) =>
                    {
                        return (int)prms["@exInstanceID"].Value;
                    }
                );

                // Await the exception policy result:
                var policy = await tskGetPolicy;

#if false
                // If logging the method context is enabled and we have a stack trace to work with, log it:
                Task tskLoggingMethod = null;
                if (policy.LogStackContext && ctx.StackTrace != null)
                {
                    tskLoggingMethod = LogStackContext(conn, ctx, exInstanceID);
                }
#endif

                // If logging the web context is enabled and we have a web context to work with, log it:
                Task tskLoggingWeb = null;
                if (policy.LogWebContext && ctx.CapturedHttpContext != null)
                {
                    tskLoggingWeb = LogWebContext(conn, ctx, exInstanceID);
                }

                // Wait for any outstanding logging tasks:
#if false
                if (tskLoggingMethod != null) await tskLoggingMethod;
#endif
                if (tskLoggingWeb != null) await tskLoggingWeb;

                return new Tuple<byte[], int>(ctx.ExceptionID, exInstanceID);
            }
        }

#if false
        async Task LogStackContext(SqlConnection conn, ExceptionWithCapturedContext ctx, int exInstanceID)
        {
            // We require a StackTrace instance:
            if (ctx.StackTrace == null)
                return;

            // TODO(jsd): Figure out how to capture parameter values, if possible.
        }
#endif

        async Task LogWebContext(SqlConnection conn, ExceptionWithCapturedContext ctx, int exInstanceID)
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

            var tskWebApplication = ExecNonQuery(
                conn,
@"MERGE [dbo].[exWebApplication] AS target
USING (SELECT @exWebApplicationID) AS source (exWebApplicationID)
ON (target.exWebApplicationID = source.exWebApplicationID)
WHEN NOT MATCHED THEN
    INSERT ([exWebApplicationID], [MachineName], [ApplicationID], [PhysicalPath], [VirtualPath], [SiteName])
    VALUES (@exWebApplicationID,  @MachineName,  @ApplicationID,  @PhysicalPath,  @VirtualPath,  @SiteName);",
                prms =>
                {
                    AddParameterWithSize(prms, "@exWebApplicationID", SqlDbType.Binary, 20, exWebApplicationID);
                    AddParameterWithSize(prms, "@MachineName", SqlDbType.NVarChar, 96, host.MachineName);
                    AddParameterWithSize(prms, "@ApplicationID", SqlDbType.VarChar, 96, host.ApplicationID);
                    AddParameterWithSize(prms, "@PhysicalPath", SqlDbType.NVarChar, 256, host.PhysicalPath);
                    AddParameterWithSize(prms, "@VirtualPath", SqlDbType.NVarChar, 256, host.VirtualPath);
                    AddParameterWithSize(prms, "@SiteName", SqlDbType.VarChar, 96, host.SiteName);
                }
            );

            // Log the web context:
            var tskContextWeb = ExecNonQuery(
                conn,
@"INSERT INTO [dbo].[exContextWeb]
       ([exInstanceID], [exWebApplicationID], [AuthenticatedUserName], [HttpVerb], [RequestURLQueryID], [ReferrerURLQueryID])
VALUES (@exInstanceID,  @exWebApplicationID,  @AuthenticatedUserName,  @HttpVerb,  @RequestURLQueryID,  @ReferrerURLQueryID);",
                prms =>
                {
                    AddParameter(prms, "@exInstanceID", SqlDbType.Int, exInstanceID);
                    // Hosting environment:
                    AddParameterWithSize(prms, "@exWebApplicationID", SqlDbType.Binary, 20, exWebApplicationID);
                    // Request details:
                    AddParameterWithSize(prms, "@AuthenticatedUserName", SqlDbType.VarChar, 96, AsDBNull(authUserName));
                    AddParameterWithSize(prms, "@HttpVerb", SqlDbType.VarChar, 16, http.HttpMethod);
                    AddParameterWithSize(prms, "@RequestURLQueryID", SqlDbType.Binary, 20, requestURLQueryID);
                    AddParameterWithSize(prms, "@ReferrerURLQueryID", SqlDbType.Binary, 20, AsDBNull(referrerURLQueryID));
                }
            );

            // Log the URLs:
            Task tskRequestURL, tskReferrerURL;
            tskRequestURL = LogURLQuery(conn, http.Url);
            if (http.UrlReferrer != null)
                tskReferrerURL = LogURLQuery(conn, http.UrlReferrer);
            else
                tskReferrerURL = null;

            // Await the completion of the tasks:
            await tskRequestURL;
            if (tskReferrerURL != null) await tskReferrerURL;
            await tskWebApplication;
            await tskContextWeb;
        }

        static byte[] CalcWebApplicationID(WebHostingContext host)
        {
            byte[] id = SHA1Hash(
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

        /// <summary>
        /// Writes a URL without query-string to the exURL table.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        async Task LogURL(SqlConnection conn, Uri uri)
        {
            byte[] urlID = CalcURLID(uri);

            await ExecNonQuery(
                conn,
@"MERGE [dbo].[exURL] AS target
USING (SELECT @exURLID) AS source (exURLID)
ON (target.exURLID = source.exURLID)
WHEN NOT MATCHED THEN
   INSERT ([exURLID], [HostName], [PortNumber], [AbsolutePath], [Scheme])
   VALUES (@exURLID,  @HostName,  @PortNumber,  @AbsolutePath,  @Scheme);",
                prms =>
                {
                    AddParameterWithSize(prms, "@exURLID", SqlDbType.Binary, 20, urlID);
                    AddParameterWithSize(prms, "@HostName", SqlDbType.VarChar, 128, uri.Host);
                    AddParameter(prms, "@PortNumber", SqlDbType.Int, (int)uri.Port);
                    AddParameterWithSize(prms, "@AbsolutePath", SqlDbType.VarChar, 512, uri.AbsolutePath);
                    AddParameterWithSize(prms, "@Scheme", SqlDbType.VarChar, 8, uri.Scheme);
                }
            );
        }

        /// <summary>
        /// Writes a URL with query-string to the exURLQuery table.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        async Task LogURLQuery(SqlConnection conn, Uri uri)
        {
            // Log the base URL:
            byte[] urlID = CalcURLID(uri);

            // Compute the URLQueryID:
            byte[] urlQueryID = CalcURLQueryID(uri);

            // Store the exURL record:
            var tskLogURL = LogURL(conn, uri);

            // Store the exURLQuery record:
            var tskLogURLQuery = ExecNonQuery(
                conn,
@"MERGE [dbo].[exURLQuery] AS target
USING (SELECT @exURLQueryID) AS source (exURLQueryID)
ON (target.exURLQueryID = source.exURLQueryID)
WHEN NOT MATCHED THEN
    INSERT ([exURLQueryID], [exURLID], [QueryString])
    VALUES (@exURLQueryID,  @exURLID,  @QueryString);",
                prms =>
                {
                    AddParameterWithSize(prms, "@exURLQueryID", SqlDbType.Binary, 20, urlQueryID);
                    AddParameterWithSize(prms, "@exURLID", SqlDbType.Binary, 20, urlID);
                    AddParameterWithSize(prms, "@QueryString", SqlDbType.VarChar, -1, AsDBNull(uri.Query));
                }
            );

            await tskLogURLQuery;
            await tskLogURL;
        }

        static byte[] CalcURLID(Uri uri)
        {
            byte[] id = SHA1Hash("{0}://{1}:{2}{3}".F(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath));
            Debug.Assert(id.Length == 20);
            return id;
        }

        static byte[] CalcURLQueryID(Uri uri)
        {
            byte[] id = SHA1Hash("{0}://{1}:{2}{3}{4}".F(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri.Query));
            Debug.Assert(id.Length == 20);
            return id;
        }

        #region Database helper methods

        static object AsDBNull<T>(T value) where T : class
        {
            if (value == null) return DBNull.Value;
            return value;
        }

        static object AsDBNull<T>(Nullable<T> value) where T : struct
        {
            if (!value.HasValue) return DBNull.Value;
            return value.Value;
        }

        static async Task<TResult> ExecReader<TResult>(
            SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlDataReader, SqlCommand, Task<TResult>> read
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr, cmd);
            }
        }

        static async Task<TResult> ExecReader<TResult>(
            SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlDataReader, Task<TResult>> read
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr);
            }
        }

        static async Task<TResult> ExecReader<TResult>(
            SqlConnection conn,
            string text,
            Func<SqlDataReader, Task<TResult>> read
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                using (var dr = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                    return await read(dr);
            }
        }

        static async Task<int> ExecNonQuery(
            SqlConnection conn,
            string text
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        static async Task<int> ExecNonQuery(
            SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                return await cmd.ExecuteNonQueryAsync();
            }
        }

        static async Task<TResult> ExecNonQuery<TResult>(
            SqlConnection conn,
            string text,
            Action<SqlParameterCollection> bindParameters,
            Func<SqlParameterCollection, int, TResult> report
        )
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = text;
                bindParameters(cmd.Parameters);
                int rc = await cmd.ExecuteNonQueryAsync();
                return report(cmd.Parameters, rc);
            }
        }

        static SqlParameter AddParameterOut(SqlParameterCollection prms, string name, SqlDbType type)
        {
            var prm = prms.Add(name, type);
            prm.Direction = ParameterDirection.Output;
            return prm;
        }

        static SqlParameter AddParameter(SqlParameterCollection prms, string name, SqlDbType type, object value)
        {
            var prm = prms.AddWithValue(name, value);
            prm.SqlDbType = type;
            return prm;
        }

        static SqlParameter AddParameterWithSize(SqlParameterCollection prms, string name, SqlDbType type, int size, object value)
        {
            var prm = prms.AddWithValue(name, value);
            prm.SqlDbType = type;
            prm.Size = size;
            return prm;
        }

        #endregion

        internal static byte[] SHA1Hash(string p)
        {
            using (var sha1 = System.Security.Cryptography.SHA1Managed.Create())
                return sha1.ComputeHash(new UTF8Encoding(false).GetBytes(p));
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
