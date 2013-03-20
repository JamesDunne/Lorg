using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public sealed class WrappedException : Exception
    {
        public readonly Exception Wrapped;
        public readonly IDictionary<string, string> UserState;

        public WrappedException(Exception ex, IDictionary<string, string> userState)
        {
            Wrapped = ex;
            // null is ok:
            UserState = userState;
        }
    }
}

namespace System
{
    public static class ExceptionExtensions
    {
        public static Lorg.WrappedException With(this Exception ex, IDictionary<string, string> userState)
        {
            return new Lorg.WrappedException(ex, userState);
        }
    }
}
