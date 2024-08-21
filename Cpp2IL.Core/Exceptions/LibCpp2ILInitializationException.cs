using System;

namespace Cpp2IL.Core.Exceptions;

public class LibCpp2ILInitializationException : Exception
{
    public LibCpp2ILInitializationException(string message, Exception innerException) : base(message, innerException)
    {
        }
}