using System;

namespace Cpp2IL.Core.Exceptions;

public class DllSaveException(string fullPath, Exception cause)
    : Exception($"Fatal Exception writing DLL {fullPath}", cause)
{
    public string FullPath { get; } = fullPath;
    public Exception Cause { get; } = cause;
}