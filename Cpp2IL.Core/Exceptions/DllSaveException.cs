using System;

namespace Cpp2IL.Core.Exceptions;

public class DllSaveException : Exception
{
    public string FullPath { get; }
    public Exception Cause { get; }

    public DllSaveException(string fullPath, Exception cause) : base($"Fatal Exception writing DLL {fullPath}", cause)
    {
            FullPath = fullPath;
            Cause = cause;
        }
}