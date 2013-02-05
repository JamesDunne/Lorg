using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace Lorg.Mvc4
{
    public sealed class LogExceptionsAttribute : FilterAttribute, IExceptionFilter
    {
        public void OnException(ExceptionContext filterContext)
        {
            var ctx = ExceptionLogger.Capture(
                filterContext.Exception,
                new Lorg.CapturedHttpContext(filterContext.HttpContext),
                false,
                null
            );

            // TODO(jsd): What to do with `ILogIdentifier` return value?
            Task.Run(async () => await ExceptionLogger.Log(ctx));

            filterContext.ExceptionHandled = false;
        }
    }
}