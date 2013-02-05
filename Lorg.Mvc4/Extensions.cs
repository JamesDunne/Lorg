using Lorg;
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
        public static async Task<ILogIdentifier> Try(this Controller ctlr, Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            ExceptionWithCapturedContext exlog;

            try
            {
                await a();
                return (ILogIdentifier)null;
            }
            catch (Exception ex)
            {
                exlog = ExceptionLogger.Capture(ex, new CapturedHttpContext(ctlr.HttpContext), isHandled, correlationID);
            }

            return await ExceptionLogger.Log(exlog);
        }

        public static async Task<ILogIdentifier> Try(this HttpContextBase ctx, Func<Task> a, bool isHandled = false, Guid? correlationID = null)
        {
            ExceptionWithCapturedContext exlog;

            try
            {
                await a();
                return (ILogIdentifier)null;
            }
            catch (Exception ex)
            {
                exlog = ExceptionLogger.Capture(ex, new CapturedHttpContext(ctx), isHandled, correlationID);
            }

            return await ExceptionLogger.Log(exlog);
        }
    }
}
