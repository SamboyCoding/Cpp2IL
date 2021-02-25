using System;

namespace Cpp2IL.Analysis
{
    public class AnalysisExceptionRaisedException : Exception
    {
        public AnalysisExceptionRaisedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}