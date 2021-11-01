using System;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class PropertyWriteFailedException : Exception
    {
        public PropertyWriteFailedException(PropertyDefinition propertyDefinition, Exception cause) : base($"Failed to write property {propertyDefinition} due to an exception", cause)
        { }
    }
}