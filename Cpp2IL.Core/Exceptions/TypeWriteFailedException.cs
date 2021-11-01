using System;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class TypeWriteFailedException : Exception
    {
        public TypeWriteFailedException(TypeDefinition typeDefinition, Exception cause) : base($"Failed to write type {typeDefinition} due to an exception", cause)
        { }
    }
}