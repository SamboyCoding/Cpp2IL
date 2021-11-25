using System;
using Cpp2IL.Core.Utils;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class TypeSignatureWriteFailedException : Exception
    {
        public TypeSignatureWriteFailedException(TypeReference type, Exception cause) : base($"Failed to write type {type} of etype {type.GetEType()} due to an exception", cause)
        { }
    }
}