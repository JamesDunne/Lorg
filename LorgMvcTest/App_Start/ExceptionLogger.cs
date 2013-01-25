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
        public static readonly Lorg.Logger Log = new Lorg.Logger(Lorg.Logger.ValidateConfiguration(new Lorg.Logger.Configuration()
            {
                ApplicationName = "LorgMvcTest",
                EnvironmentName = "Local",
                ConnectionString = new System.Data.SqlClient.SqlConnectionStringBuilder()
                {
                    InitialCatalog = "Lorg",
                    DataSource = ".",
                    IntegratedSecurity = true,
                }.ToString()
            }
        ));

        static Lorg.WebHostingContext hostingContext = new Lorg.WebHostingContext();

        public static void CreateLogger()
        {
            // Dummy method to let static ctor do its thing.
        }

        public static void CaptureAndLog(Exception ex, Lorg.CapturedHttpContext httpContext, bool isHandled = false, Guid? correlationID = null)
        {
            // Capture the thread-specific bits first before we jump into async execution:
            var context = new Lorg.ExceptionWithCapturedContext(
                ex,
                isHandled: isHandled,
                correlationID: correlationID,
                webHostingContext: hostingContext,
                capturedHttpContext: httpContext
            );

            // Run the exception logger asynchronously:
            Task.Run(async () => await Log.Write(context));
        }

        public static async Task HandleExceptions(Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            try
            {
                await a();
            }
            catch (Exception ex)
            {
                CaptureAndLog(ex, new Lorg.CapturedHttpContext(HttpContext.Current), isHandled, correlationID);
            }
        }
    }
}