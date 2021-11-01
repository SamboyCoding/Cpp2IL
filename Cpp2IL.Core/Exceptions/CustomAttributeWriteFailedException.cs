using System;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class CustomAttributeWriteFailedException : Exception
    {
        public CustomAttributeWriteFailedException(CustomAttribute customAttribute, Exception cause) : base($"Failed to write custom attribute {customAttribute} due to an exception", cause)
        { }
    }
}