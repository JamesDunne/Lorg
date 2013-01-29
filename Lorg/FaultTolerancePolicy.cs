using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public class FaultTolerancePolicy
    {
#pragma warning disable 1998
        protected virtual async Task HandleException(ExceptionWithCapturedContext ex)
        {
            Trace.WriteLine(ex.Exception.ToString());
        }
#pragma warning restore 1998

        public virtual async Task<Maybe<TResult>> Try<TResult>(Func<Task<TResult>> action)
        {
            ExceptionWithCapturedContext toHandle = null;

            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                toHandle = new ExceptionWithCapturedContext(ex);
            }

            if (toHandle != null)
                await HandleException(toHandle);
            return Maybe<TResult>.Nothing;
        }
    }
}
