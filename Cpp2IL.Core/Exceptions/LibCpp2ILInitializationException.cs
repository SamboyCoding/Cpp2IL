using System;

namespace Cpp2IL.Core.Exceptions;

public class LibCpp2ILInitializationException(string message, Exception innerException)
    : Exception(message, innerException);
