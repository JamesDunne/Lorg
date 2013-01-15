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
                ConnectionString = csb.ToString()
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

        public async Task WriteDatabase(Exception ex)
        {
            //ex.StackTrace;
            //ex.TargetSite;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = @"
MERGE INTO 
";
                await cmd.ExecuteNonQueryAsync();
            }
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
    }
}
