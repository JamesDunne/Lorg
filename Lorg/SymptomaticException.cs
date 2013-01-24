using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    internal sealed class LoggerExceptionWithCapturedContext
    {
        internal ExceptionWithCapturedContext Actual { get; private set; }
        internal ExceptionWithCapturedContext Symptom { get; private set; }

        internal LoggerExceptionWithCapturedContext(ExceptionWithCapturedContext actual, ExceptionWithCapturedContext symptom)
        {
            Actual = actual;
            Symptom = symptom;
        }
    }
}
