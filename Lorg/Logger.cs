using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public sealed class Logger
    {
        static string nl = "\n";

        ValidConfiguration cfg;
        SqlConnection conn;
        bool noConnection = false;

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

            // Ensure that AsynchronousProcessing is enabled:
            var csb = new SqlConnectionStringBuilder(cfg.ConnectionString);
            csb.AsynchronousProcessing = true;

            // Return the ValidConfiguration type that asserts we validated the user config:
            return new ValidConfiguration
            {
                ApplicationName = cfg.ApplicationName,
                EnvironmentName = cfg.EnvironmentName,
                ConnectionString = csb.ToString(),

                MachineName = Environment.MachineName,
                ProcessPath = "", // TODO(jsd)
            };
        }

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

        public async Task Write(Exception ex)
        {
            bool failureMode = noConnection;
            Exception symptom = null;

            if (!failureMode)
            {
                try
                {
                    await WriteDatabase(ex);
                }
                catch (Exception ourEx)
                {
                    // Hmm...
                    failureMode = true;
                    symptom = ourEx;
                }
            }

            if (failureMode)
            {
                Exception report = ex;
                if (symptom != null)
                    report = new SymptomaticException(symptom, ex);
                await FailoverWrite(report);
            }
        }

        public static string FormatException(Exception ex, int indent = 0)
        {
            if (ex == null) return "(null)";

            var sb = new StringBuilder();
            Format(ex, sb, 0);
            return sb.ToString();
        }

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

        /// <summary>
        /// The failover case for when nothing seems to be set up as expected.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        public static async Task FailoverWrite(Exception ex)
        {
            string output = FormatException(ex);
            // Write to what we know:
            Debug.WriteLine(output);
            Trace.WriteLine(output);
            await Console.Error.WriteLineAsync(output);
        }

        async Task<Tuple<byte[], int>> WriteDatabase(Exception ex)
        {
            Debug.Assert(ex != null);

            var loggedTimeUTC = DateTime.UtcNow;

            var exType = ex.GetType();
            var typeName = exType.FullName;
            var assemblyName = exType.Assembly.FullName;
            // TODO(jsd): Sanitize embedded file paths in stack trace
            var stackTrace = ex.StackTrace;

            // SHA1 hash the `assemblyName:typeName:stackTrace`:
            var exExceptionID = SHA1Hash(String.Concat(assemblyName, ":", typeName, ":", stackTrace));

            using (var conn = new SqlConnection(cfg.ConnectionString))
            {
                await conn.OpenAsync();

                // Check if the exException record exists:
                var exists = await ExecReader(
                    conn,
@"SELECT [exExceptionID] FROM [dbo].[exException] WITH (NOLOCK) WHERE exExceptionID = @exExceptionID",
                    prms =>
                    {
                        AddParameterWithSize(prms, "@exExceptionID", SqlDbType.Binary, 20, exExceptionID);
                    },
                    (dr, cmd) => dr.ReadAsync()
                );

                // Create the exException record if it does not exist:
                if (!exists)
                {
                    await ExecNonQuery(
                        conn,
@"INSERT INTO [dbo].[exException] ([exExceptionID], [AssemblyName], [TypeName], [StackTrace]) VALUES (@exExceptionID, @assemblyName, @typeName, @stackTrace);
SET @exInstanceID = SCOPE_IDENTITY();",
                        prms =>
                        {
                            AddParameterOut(prms, "@exInstanceID", SqlDbType.Int);
                            AddParameterWithSize(prms, "@exExceptionID", SqlDbType.Binary, 20, exExceptionID);
                            AddParameterWithSize(prms, "@assemblyName", SqlDbType.NVarChar, 256, assemblyName);
                            AddParameterWithSize(prms, "@typeName", SqlDbType.NVarChar, 256, typeName);
                            AddParameterWithSize(prms, "@stackTrace", SqlDbType.NVarChar, -1, stackTrace);
                        }
                    );
                }

                // Create the instance record:
                int exInstanceID = await ExecNonQuery(
                    conn,
@"INSERT INTO [dbo].[exInstance]
    ([exExceptionID], [LoggedTimeUTC], [IsHandled], [CorrelationID], [Message], [ApplicationName], [MachineName], [ProcessPath], [ExecutingAssemblyName], [ManagedThreadId])
VALUES (@exExceptionID, @loggedTimeUTC, @isHandled, @correlationID, @message, @applicationName, @machineName, @processPath, @executingAssemblyName, @managedThreadId);
SET @exInstanceID = SCOPE_IDENTITY();",
                    prms =>
                    {
                        AddParameterOut(prms, "@exInstanceID", SqlDbType.Int);
                        AddParameterWithSize(prms, "@exExceptionID", SqlDbType.Binary, 20, exExceptionID);
                        AddParameter(prms, "@loggedTimeUTC", SqlDbType.DateTime2, loggedTimeUTC);

                        // TODO(jsd): IsHandled and CorrelationID
                        AddParameter(prms, "@isHandled", SqlDbType.Bit, false);
                        AddParameter(prms, "@correlationID", SqlDbType.UniqueIdentifier, Guid.NewGuid());

                        AddParameterWithSize(prms, "@message", SqlDbType.NVarChar, 256, ex.Message);
                        AddParameterWithSize(prms, "@applicationName", SqlDbType.VarChar, 96, cfg.ApplicationName);
                        AddParameterWithSize(prms, "@machineName", SqlDbType.VarChar, 64, cfg.MachineName);
                        AddParameterWithSize(prms, "@processPath", SqlDbType.NVarChar, 256, cfg.ProcessPath);
                        AddParameterWithSize(prms, "@executingAssemblyName", SqlDbType.NVarChar, 256, ex.TargetSite.DeclaringType.Assembly.FullName);
                        AddParameter(prms, "@ManagedThreadId", SqlDbType.Int, System.Threading.Thread.CurrentThread.ManagedThreadId);
                    },
                    (prms, rc) =>
                    {
                        return (int)prms["@exInstanceID"].Value;
                    }
                );

                return new Tuple<byte[], int>(exExceptionID, exInstanceID);
            }
        }

        #region Implementation details

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

        static byte[] SHA1Hash(string p)
        {
            using (var sha1 = System.Security.Cryptography.SHA1Managed.Create())
                return sha1.ComputeHash(new UTF8Encoding(false).GetBytes(p));
        }

        #endregion
    }
}
