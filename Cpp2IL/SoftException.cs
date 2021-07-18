using System;

namespace Cpp2IL.Core
{
    public class SoftException : Exception
    {
        public SoftException(string? message) : base(message)
        {
        }
    }
}