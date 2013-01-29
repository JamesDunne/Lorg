using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lorg
{
    /// <summary>
    /// Represents a thrown exception and some captured context from the catching thread.
    /// </summary>
    public class ExceptionWithCapturedContext
    {
        internal readonly Exception Exception;
        internal readonly ExceptionWithCapturedContext InnerException;

        internal readonly bool IsHandled;
        internal readonly Guid? CorrelationID;
        internal readonly CapturedHttpContext CapturedHttpContext;
        internal readonly WebHostingContext WebHostingContext;

        internal readonly byte[] ExceptionID;
        internal readonly string AssemblyName;
        internal readonly string TypeName;
        internal readonly string StackTrace;
        internal readonly ExceptionTargetSite TargetSite;

        internal readonly StackTrace RealStackTrace;

        internal readonly DateTime LoggedTimeUTC;
        internal readonly int ManagedThreadID;
        internal readonly int SequenceNumber;

        internal static int runningSequenceNumber = 0;

        /// <summary>
        /// Represents a thrown exception and some context.
        /// </summary>
        /// <param name="ex">The exception that was thrown.</param>
        /// <param name="isHandled">Whether the exception is explicitly handled or not.</param>
        /// <param name="correlationID">A unique identifier to tie two or more exception reports together.</param>
        /// <param name="webHostingContext">The current web application hosting context, if applicable.</param>
        /// <param name="capturedHttpContext">Captured values from the current HTTP context being handled, if applicable.</param>
        public ExceptionWithCapturedContext(
            Exception ex,
            bool isHandled = false,
            Guid? correlationID = null,
            WebHostingContext webHostingContext = null,
            CapturedHttpContext capturedHttpContext = null
        )
        {
            Exception = ex;
            IsHandled = isHandled;
            CorrelationID = correlationID;
            WebHostingContext = webHostingContext;
            CapturedHttpContext = capturedHttpContext;

            RealStackTrace = new StackTrace(ex, true);

            LoggedTimeUTC = DateTime.UtcNow;
            ManagedThreadID = Thread.CurrentThread.ManagedThreadId;
            SequenceNumber = Interlocked.Increment(ref runningSequenceNumber);

            // Capture some details about the exception:
            var exType = ex.GetType();
            AssemblyName = exType.Assembly.FullName;
            TypeName = exType.FullName;
            // TODO(jsd): Sanitize embedded file paths in stack trace
            StackTrace = ex.StackTrace;

            // SHA1 hash the `assemblyName:typeName:stackTrace[:targetSite]`:
            string hashableData = String.Concat(AssemblyName, ":", TypeName, ":", StackTrace);

            if (RealStackTrace != null && RealStackTrace.FrameCount > 0)
            {
                // Add in TargetSite data:
                TargetSite = new ExceptionTargetSite(RealStackTrace);
                hashableData += ":" + TargetSite.GetHashableData();
            }

            ExceptionID = Hash.SHA1(hashableData);

            // Recursively capture context for any InnerExceptions:
            if (ex.InnerException != null)
                InnerException = new ExceptionWithCapturedContext(ex.InnerException, isHandled, correlationID, webHostingContext, capturedHttpContext);
        }
    }
}
