using System;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class MetadataTokenCompressionException : Exception
    {
        public MetadataTokenCompressionException(CecilCodedIndex codedIndex, MetadataToken token, Exception cause) : base($"Failed to compress metadata token {token} (of type {token.TokenType}, RID {token.RID}) into coded index of type {codedIndex}, due to an exception", cause)
        { }
    }
}