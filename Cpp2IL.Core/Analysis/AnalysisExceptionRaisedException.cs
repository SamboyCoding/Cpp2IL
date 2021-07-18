using System;

namespace Cpp2IL.Core.Analysis
{
    public class AnalysisExceptionRaisedException : Exception
    {
        public AnalysisExceptionRaisedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}