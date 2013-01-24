using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace LorgMvcTest
{
    public static class ExceptionLogger
    {
        /// <summary>
        /// Default instance of exception logger.
        /// </summary>
        public static readonly Lorg.Logger Log;

        public static void CreateLogger()
        {
            // Dummy method to let static ctor do its thing.
        }

        public static Task HandleExceptions(Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            return Log.HandleExceptions(a, isHandled, correlationID);
        }

        static ExceptionLogger()
        {
            Log = Lorg.Logger.AttemptInitialize(new Lorg.Logger.Configuration()
            {
                ApplicationName = "LorgMvcTest",
                EnvironmentName = "Local",
                ConnectionString = new System.Data.SqlClient.SqlConnectionStringBuilder()
                {
                    InitialCatalog = "Lorg",
                    DataSource = ".",
                    IntegratedSecurity = true,
                }.ToString()
            });
        }
    }
}