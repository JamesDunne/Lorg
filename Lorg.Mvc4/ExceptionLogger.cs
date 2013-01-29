using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Lorg.Mvc4
{
    public static class ExceptionLogger
    {
        /// <summary>
        /// Default instance of exception logger.
        /// </summary>
        public static Logger Log { get; private set; }

        static WebHostingContext hostingContext = new WebHostingContext();

        public static void CreateLogger(Logger.Configuration cfg)
        {
            Log = new Logger(Logger.ValidateConfiguration(cfg));
        }

        public static void CaptureAndLog(Exception ex, CapturedHttpContext httpContext, bool isHandled = false, Guid? correlationID = null)
        {
            // Capture the thread-specific bits first before we jump into async execution:
            var context = new ExceptionWithCapturedContext(
                ex,
                isHandled: isHandled,
                correlationID: correlationID,
                webHostingContext: hostingContext,
                capturedHttpContext: httpContext
            );

            // Run the exception logger asynchronously:
            Task.Run(async () => await Log.Write(context));
        }
    }
}