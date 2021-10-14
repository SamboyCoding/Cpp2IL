using System;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class MethodWriteFailedException : Exception
    {
        public MethodWriteFailedException(MethodDefinition methodDefinition, Exception cause) : base($"Failed to write body for method {methodDefinition} due to an exception", cause)
        { }
    }
}