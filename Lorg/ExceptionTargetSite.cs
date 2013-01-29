using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lorg
{
    public sealed class ExceptionTargetSite
    {
        public readonly string AssemblyName;
        public readonly string TypeName;
        public readonly string MethodName;
        public readonly int ILOffset;

        public readonly string FileName;
        public readonly int FileLineNumber;
        public readonly int FileColumnNumber;

        public readonly byte[] TargetSiteID;

        internal ExceptionTargetSite(StackTrace trace)
        {
            Debug.Assert(trace != null);
            Debug.Assert(trace.FrameCount > 0);

            var frame = trace.GetFrame(0);
            Debug.Assert(frame != null);

            var method = frame.GetMethod();
            Debug.Assert(method != null);

            AssemblyName = method.DeclaringType.Assembly.FullName;
            TypeName = method.DeclaringType.FullName;
            MethodName = method.Name;
            ILOffset = frame.GetILOffset();
            FileName = frame.GetFileName();
            FileLineNumber = frame.GetFileLineNumber();
            FileColumnNumber = frame.GetFileColumnNumber();

            TargetSiteID = Hash.SHA1(GetHashableData());
        }

        internal string GetHashableData()
        {
            return String.Concat(
                AssemblyName, ":",
                TypeName, ":",
                MethodName, ":",
                ILOffset.ToString(), ":",
                FileName, ":",
                FileLineNumber.ToString(), ":",
                FileColumnNumber.ToString()
            );
        }
    }
}
