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
        static Lorg.Logger.WebHostingContext hostingContext = new Lorg.Logger.WebHostingContext();

        public void OnException(ExceptionContext filterContext)
        {
            var context = new Lorg.Logger.ExceptionThreadContext(
                filterContext.Exception,
                isHandled: false,
                correlationID: null,
                stackTrace: new System.Diagnostics.StackTrace(filterContext.Exception, true),
                webHostingContext: hostingContext,
                capturedHttpContext: new Lorg.Logger.CapturedHttpContext(filterContext.HttpContext)
            );

            Task.Run(async () =>
            {
                await ExceptionLogger.Log.Write(context);
            });

            filterContext.ExceptionHandled = true;
        }
    }
}