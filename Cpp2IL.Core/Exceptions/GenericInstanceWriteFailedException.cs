using System;
using System.Linq;
using Mono.Cecil;

namespace Cpp2IL.Core.Exceptions
{
    public class GenericInstanceWriteFailedException : Exception
    {
        public GenericInstanceWriteFailedException(IGenericInstance instance, Exception cause) 
            : base($"Failed to write generic instance {instance} due to an exception", cause)
        { }
    }
}