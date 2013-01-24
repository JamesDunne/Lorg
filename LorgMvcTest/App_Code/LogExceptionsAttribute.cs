using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace LorgMvcTest
{
    public sealed class LogExceptionsAttribute : FilterAttribute, IExceptionFilter
    {
        static Lorg.WebHostingContext hostingContext = new Lorg.WebHostingContext();

        public void OnException(ExceptionContext filterContext)
        {
            // Capture the thread-specific bits first before we jump into async execution:
            var context = new Lorg.ExceptionWithCapturedContext(
                filterContext.Exception,
                isHandled: false,
                correlationID: null,
                webHostingContext: hostingContext,
                capturedHttpContext: new Lorg.CapturedHttpContext(filterContext.HttpContext)
            );

            // Run the exception logger asynchronously:
            Task.Run(async () =>
            {
                await ExceptionLogger.Log.Write(context);
            });

            // Mark the exception as handled:
            filterContext.ExceptionHandled = true;

            // TODO: still need to produce a result!
        }
    }
}