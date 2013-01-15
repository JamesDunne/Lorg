using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public sealed class SymptomaticException : Exception
    {
        public Exception Symptom { get; private set; }
        public Exception Actual { get; private set; }

        public SymptomaticException(Exception symptom, Exception actual)
        {
            Symptom = symptom;
            Actual = actual;
        }
    }
}
