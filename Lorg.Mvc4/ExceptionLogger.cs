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
        static Logger logger;

        static WebHostingContext hostingContext = new WebHostingContext();

        public static void CreateLogger(Logger.Configuration cfg)
        {
            logger = new Logger(Logger.ValidateConfiguration(cfg));
        }

        public static ExceptionWithCapturedContext Capture(Exception ex, CapturedHttpContext httpContext, bool isHandled = false, Guid? correlationID = null)
        {
            // Capture the thread-specific bits first before we jump into async execution:
            var context = new ExceptionWithCapturedContext(
                ex,
                isHandled: isHandled,
                correlationID: correlationID,
                webHostingContext: hostingContext,
                capturedHttpContext: httpContext
            );

            return context;
        }

        public static Task<ILogIdentifier> Log(ExceptionWithCapturedContext context)
        {
            // Run the exception logger asynchronously:
            return Task.Run(async () => await logger.Write(context));
        }
    }
}