using Lorg.Mvc4;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Web.Mvc
{
    public static class Extensions
    {
        public static async Task<bool> Try(this Controller ctlr, Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            try
            {
                await a();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionLogger.CaptureAndLog(ex, new Lorg.CapturedHttpContext(ctlr.HttpContext), isHandled, correlationID);
                return false;
            }
        }

        public static async Task<bool> Try(this HttpContextBase ctx, Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            try
            {
                await a();
                return true;
            }
            catch (Exception ex)
            {
                ExceptionLogger.CaptureAndLog(ex, new Lorg.CapturedHttpContext(ctx), isHandled, correlationID);
                return false;
            }
        }
    }
}
