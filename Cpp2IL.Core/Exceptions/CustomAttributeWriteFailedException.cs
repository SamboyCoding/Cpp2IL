using System;
using System.Linq;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class CustomAttributeWriteFailedException : Exception
    {
        public CustomAttributeWriteFailedException(CustomAttribute customAttribute, Exception cause) 
            : base($"Failed to write custom attribute {customAttribute.Constructor.DeclaringType} with arguments [{string.Join(", ", customAttribute.ConstructorArguments.Select(a => a.Value + " of type " + a.Type))}] due to an exception", cause)
        { }
    }
}