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
        ValidConfiguration cfg;
        SqlConnection conn;
        bool noConnection = false;
        int sequenceNumber = 0;

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
        }

        public class WebHostingContext
        {
            public string ApplicationID { get; private set; }
            public string ApplicationPhysicalPath { get; private set; }
            public string ApplicationVirtualPath { get; private set; }
            public string SiteName { get; private set; }

            /// <summary>
            /// Initializes the WebHostingContext with values pulled from `System.Web.Hosting.HostingEnvironment`.
            /// </summary>
            public WebHostingContext()
            {
                ApplicationID = System.Web.Hosting.HostingEnvironment.ApplicationID;
                ApplicationPhysicalPath = System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath;
                ApplicationVirtualPath = System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath;
                SiteName = System.Web.Hosting.HostingEnvironment.SiteName;
            }
        }

        public class CapturedHttpContext
        {
            internal readonly Uri Url;
            internal readonly Uri UrlReferrer;
            internal readonly System.Security.Principal.IPrincipal User;
            internal readonly string HttpMethod;

            public CapturedHttpContext(System.Web.HttpContextBase httpContext)
            {
                Url = httpContext.Request.Url;
                UrlReferrer = httpContext.Request.UrlReferrer;
                User = httpContext.User;
                HttpMethod = httpContext.Request.HttpMethod;
            }

            public CapturedHttpContext(System.Web.HttpContext httpContext)
            {
                Url = httpContext.Request.Url;
                UrlReferrer = httpContext.Request.UrlReferrer;
                User = httpContext.User;
                HttpMethod = httpContext.Request.HttpMethod;
            }
        }

        /// <summary>
        /// Represents a thrown exception and some context.
        /// </summary>
        public class ExceptionThreadContext
        {
            internal readonly Exception Exception;
            internal readonly bool IsHandled;
            internal readonly Guid? CorrelationID;
            internal readonly StackTrace StackTrace;
            internal readonly CapturedHttpContext CapturedHttpContext;
            internal readonly WebHostingContext WebHostingContext;

            /// <summary>
            /// Represents a thrown exception and some context.
            /// </summary>
            /// <param name="ex">The exception that was thrown.</param>
            /// <param name="isHandled">Whether the exception is explicitly handled or not.</param>
            /// <param name="correlationID">A unique identifier to tie two or more exception reports together.</param>
            /// <param name="stackTrace">The stack trace (only used for method context logging; detailed parameters).</param>
            /// <param name="httpContext">The current HTTP context being handled, if applicable.</param>
            /// <param name="webHostingContext">The current web application hosting context, if applicable.</param>
            public ExceptionThreadContext(
                Exception ex,
                bool isHandled = false,
                Guid? correlationID = null,
                StackTrace stackTrace = null,
                WebHostingContext webHostingContext = null,
                CapturedHttpContext capturedHttpContext = null
            )
            {
                Exception = ex;
                IsHandled = isHandled;
                CorrelationID = correlationID;
                StackTrace = stackTrace;
                WebHostingContext = webHostingContext;
                CapturedHttpContext = capturedHttpContext;
            }
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
        /// Attempt to initialize the logger with the given configuration.
        /// </summary>
        /// <param name="cfg"></param>
        /// <returns></returns>
        public static Logger AttemptInitialize(Configuration cfg)
        {
            Logger log = new Logger();

            try
            {
                ValidConfiguration valid = Logger.ValidateConfiguration(cfg);
                log.Initialize(valid).Wait();
            }
            catch (Exception ex)
            {
                log.FailoverWrite(ex).Wait();
            }

            return log;
        }

        /// <summary>
        /// Construct an instance with default configuration.
        /// </summary>
        public Logger()
        {
            this.cfg = new ValidConfiguration() { ApplicationName = String.Empty, EnvironmentName = String.Empty, ConnectionString = null };
            this.noConnection = true;
        }

        /// <summary>
        /// Initializes the logger and makes sure the environment is sane.
        /// </summary>
        /// <returns></returns>
        public async Task Initialize(ValidConfiguration cfg)
        {
            this.cfg = cfg;
            this.conn = new SqlConnection(cfg.ConnectionString);
            Exception report = null;

            try
            {
                // Open the DB connection:
                await conn.OpenAsync();
                noConnection = false;

                // TODO: Validate table schema
            }
            catch (Exception ex)
            {
                noConnection = true;
                report = ex;

                conn.Close();
                conn.Dispose();
            }

            if (report != null)
                await FailoverWrite(report);
        }

        /// <summary>
        /// Write the exception to the database.
        /// </summary>
        /// <returns></returns>
        public async Task Write(ExceptionThreadContext thrown)
        {
            var ctx = GetContext(thrown);
            bool failureMode = noConnection;
            Exception symptom = null;

            if (!failureMode)
            {
                try
                {
                    await WriteDatabase(ctx);
                }
                catch (Exception ourEx)
                {
                    failureMode = true;
                    symptom = ourEx;
                }
            }

            if (failureMode)
            {
                // TODO(jsd): Clean this up
                Exception report = thrown.Exception;
                if (symptom != null)
                    report = new SymptomaticException(symptom, thrown.Exception);
                await FailoverWrite(report);
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
            StackTrace stackTrace = null;

            try
            {
                await a();
            }
            catch (Exception ex)
            {
                // NOTE(jsd): `await` is not allowed in catch blocks.
                exToLog = ex;
                stackTrace = new StackTrace(ex, true);
            }

            if (exToLog != null)
            {
                await Write(new ExceptionThreadContext(
                    exToLog,
                    isHandled,
                    correlationID,
                    stackTrace,
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
        public async Task FailoverWrite(Exception ex)
        {
            string output = FormatException(ex);
            // Write to what we know:
            Trace.WriteLine(output);
            // Log a Windows event log entry:
            EventLog.WriteEntry(cfg.ApplicationName, output, EventLogEntryType.Error);
        }

        public static string FormatException(Exception ex)
        {
            if (ex == null) return "(null)";

            var sb = new StringBuilder();
            Format(ex, sb, 0);
            return sb.ToString();
        }

        #region Implementation details

        struct ThrownExceptionContext
        {
            internal readonly ExceptionThreadContext Thrown;

            internal readonly byte[] ExceptionID;
            internal readonly string AssemblyName;
            internal readonly string TypeName;
            internal readonly string StackTrace;

            internal readonly DateTime LoggedTimeUTC;
            internal readonly int ManagedThreadID;
            internal readonly int SequenceNumber;

            internal ThrownExceptionContext(
                ExceptionThreadContext thrown,
                byte[] exceptionID,
                string assemblyName,
                string typeName,
                string stackTrace,
                DateTime loggedTimeUTC,
                int managedThreadID,
                int sequenceNumber
            )
            {
                Thrown = thrown;
                ExceptionID = exceptionID;
                AssemblyName = assemblyName;
                TypeName = typeName;
                StackTrace = stackTrace;
                LoggedTimeUTC = loggedTimeUTC;
                ManagedThreadID = managedThreadID;
                SequenceNumber = sequenceNumber;
            }
        }

        ThrownExceptionContext GetContext(ExceptionThreadContext thrown)
        {
            var exType = thrown.Exception.GetType();
            var typeName = exType.FullName;
            var assemblyName = exType.Assembly.FullName;
            // TODO(jsd): Sanitize embedded file paths in stack trace
            var stackTrace = thrown.Exception.StackTrace;

            // SHA1 hash the `assemblyName:typeName:stackTrace`:
            var exExceptionID = SHA1Hash(String.Concat(assemblyName, ":", typeName, ":", stackTrace));

            return new ThrownExceptionContext(
                thrown,
                exExceptionID,
                assemblyName,
                typeName,
                stackTrace,
                DateTime.UtcNow,
                Thread.CurrentThread.ManagedThreadId,
                Interlocked.Increment(ref sequenceNumber)
            );
        }

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
        async Task<Tuple<byte[], int>> WriteDatabase(ThrownExceptionContext ctx)
        {
            using (var conn = new SqlConnection(cfg.ConnectionString))
            {
                await conn.OpenAsync();

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
                        AddParameter(prms, "@isHandled", SqlDbType.Bit, ctx.Thrown.IsHandled);
                        AddParameter(prms, "@correlationID", SqlDbType.UniqueIdentifier, AsDBNull(ctx.Thrown.CorrelationID));
                        AddParameterWithSize(prms, "@message", SqlDbType.NVarChar, 256, ctx.Thrown.Exception.Message);
                        AddParameterWithSize(prms, "@applicationName", SqlDbType.VarChar, 96, cfg.ApplicationName);
                        AddParameterWithSize(prms, "@environmentName", SqlDbType.VarChar, 96, cfg.EnvironmentName);
                        AddParameterWithSize(prms, "@machineName", SqlDbType.VarChar, 64, cfg.MachineName);
                        AddParameterWithSize(prms, "@processPath", SqlDbType.NVarChar, 256, cfg.ProcessPath);
                        AddParameterWithSize(prms, "@executingAssemblyName", SqlDbType.NVarChar, 256, ctx.Thrown.Exception.TargetSite.DeclaringType.Assembly.FullName);
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

                // If logging the method context is enabled and we have a stack trace to work with, log it:
                Task tskLoggingMethod = null;
                if (policy.LogStackContext && ctx.Thrown.StackTrace != null)
                {
                    tskLoggingMethod = LogStackContext(conn, ctx, exInstanceID);
                }

                // If logging the web context is enabled and we have a web context to work with, log it:
                Task tskLoggingWeb = null;
                if (policy.LogWebContext && ctx.Thrown.CapturedHttpContext != null)
                {
                    tskLoggingWeb = LogWebContext(conn, ctx, exInstanceID);
                }

                // Wait for any outstanding logging tasks:
                if (tskLoggingMethod != null) await tskLoggingMethod;
                if (tskLoggingWeb != null) await tskLoggingWeb;

                return new Tuple<byte[], int>(ctx.ExceptionID, exInstanceID);
            }
        }

        async Task LogStackContext(SqlConnection conn, ThrownExceptionContext ctx, int exInstanceID)
        {
            // We require a StackTrace instance:
            if (ctx.Thrown.StackTrace == null)
                return;

            // TODO
        }

        async Task LogWebContext(SqlConnection conn, ThrownExceptionContext ctx, int exInstanceID)
        {
            var http = ctx.Thrown.CapturedHttpContext;
            var host = ctx.Thrown.WebHostingContext;

            // We require both HTTP and Host context:
            if (http == null || host == null)
                return;

            // Try to get the authenticated user for the HTTP context:
            string authUserName = null;
            if (http.User != null && http.User.Identity != null)
                authUserName = http.User.Identity.Name;

            // Log URLs first:
            Task<byte[]> tskRequestURL, tskReferrerURL;

            tskRequestURL = LogURLQuery(conn, http.Url);
            if (http.UrlReferrer != null)
                tskReferrerURL = LogURLQuery(conn, http.UrlReferrer);
            else
                tskReferrerURL = null;

            byte[] requestURLQueryID = await tskRequestURL;
            byte[] referrerURLQueryID = tskReferrerURL == null ? null : await tskReferrerURL;

            // Log the web context:
            await ExecNonQuery(
                conn,
@"INSERT INTO [dbo].[exContextWeb]
       ([exInstanceID], [ApplicationID], [ApplicationPhysicalPath], [ApplicationVirtualPath], [SiteName], [AuthenticatedUserName], [HttpVerb], [RequestURL], [ReferrerURL])
VALUES (@exInstanceID,  @ApplicationID,  @ApplicationPhysicalPath,  @ApplicationVirtualPath,  @SiteName,  @AuthenticatedUserName,  @HttpVerb,  @RequestURL,  @ReferrerURL);",
                prms =>
                {
                    AddParameter(prms, "@exInstanceID", SqlDbType.Int, exInstanceID);
                    // Hosting environment:
                    AddParameterWithSize(prms, "@ApplicationID", SqlDbType.VarChar, 96, host.ApplicationID);
                    AddParameterWithSize(prms, "@ApplicationPhysicalPath", SqlDbType.NVarChar, 256, host.ApplicationPhysicalPath);
                    AddParameterWithSize(prms, "@ApplicationVirtualPath", SqlDbType.NVarChar, 256, host.ApplicationVirtualPath);
                    AddParameterWithSize(prms, "@SiteName", SqlDbType.VarChar, 96, host.SiteName);
                    // Request details:
                    AddParameterWithSize(prms, "@AuthenticatedUserName", SqlDbType.VarChar, 96, AsDBNull(authUserName));
                    AddParameterWithSize(prms, "@HttpVerb", SqlDbType.VarChar, 16, http.HttpMethod);
                    AddParameterWithSize(prms, "@RequestURL", SqlDbType.Binary, 20, requestURLQueryID);
                    AddParameterWithSize(prms, "@ReferrerURL", SqlDbType.Binary, 20, AsDBNull(referrerURLQueryID));
                }
            );
        }

        static byte[] GetURLID(Uri uri)
        {
            byte[] id = SHA1Hash("{0}://{1}:{2}{3}".F(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath));
            Debug.Assert(id.Length == 20);
            return id;
        }

        static byte[] GetURLQueryID(Uri uri)
        {
            byte[] id = SHA1Hash("{0}://{1}:{2}{3}{4}".F(uri.Scheme, uri.Host, uri.Port, uri.AbsolutePath, uri.Query));
            Debug.Assert(id.Length == 20);
            return id;
        }

        /// <summary>
        /// Writes a URL without query-string to the exURL table.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        async Task<byte[]> LogURL(SqlConnection conn, Uri uri)
        {
            byte[] urlID = GetURLID(uri);

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

            return urlID;
        }

        /// <summary>
        /// Writes a URL with query-string to the exURLQuery table.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        async Task<byte[]> LogURLQuery(SqlConnection conn, Uri uri)
        {
            // Log the base URL:
            byte[] urlID = await LogURL(conn, uri);

            // Compute the URLQueryID:
            byte[] urlQueryID = GetURLQueryID(uri);

            // Store the exURLQuery record:
            await ExecNonQuery(
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

            return urlQueryID;
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

        static byte[] SHA1Hash(string p)
        {
            using (var sha1 = System.Security.Cryptography.SHA1Managed.Create())
                return sha1.ComputeHash(new UTF8Encoding(false).GetBytes(p));
        }

        const string nl = "\n";

        static void Format(Exception ex, StringBuilder sb, int indent)
        {
            if (ex == null) return;

            string indentStr = new string(' ', indent * 2);
            string nlIndent = nl + indentStr;
            string indent2Str = new string(' ', (indent + 1) * 2);
            string nlIndent2 = nl + indent2Str;
            sb.Append(indentStr);

            SymptomaticException symp;
            if ((symp = ex as SymptomaticException) != null)
            {
                sb.Append("SymptomaticException:");
                sb.Append(nlIndent2);
                sb.Append("Actual:");
                sb.Append(nl);
                Format(symp.Actual, sb, indent + 2);
                sb.Append(nlIndent2);
                sb.Append("Symptom:");
                sb.Append(nl);
                Format(symp.Symptom, sb, indent + 2);
            }
            else
            {
                sb.Append(ex.ToString().Replace("\r\n", nl).Replace(nl, nlIndent));
            }

            if (ex.InnerException != null)
            {
                sb.Append(nlIndent);
                sb.Append("Inner:");
                sb.Append(nl);
                Format(ex.InnerException, sb, indent + 1);
            }
        }

        #endregion
    }
}
