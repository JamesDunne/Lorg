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
        public void OnException(ExceptionContext filterContext)
        {
            ExceptionLogger.CaptureAndLog(
                filterContext.Exception,
                new Lorg.CapturedHttpContext(filterContext.HttpContext),
                false,
                null
            );

            filterContext.ExceptionHandled = false;
        }
    }
}