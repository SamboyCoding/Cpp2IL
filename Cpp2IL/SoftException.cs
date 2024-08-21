using System;

namespace Cpp2IL;

public class SoftException : Exception
{
    public SoftException(string? message) : base(message)
    {
    }
}