using System;

namespace Cpp2IL.Core.Exceptions;

public class IsilConversionException : Exception
{
    public string Reason { get; }

    public IsilConversionException(string message) : base($"Failed to convert to ISIL. Reason: {message}")
    {
        Reason = message;
    }
}