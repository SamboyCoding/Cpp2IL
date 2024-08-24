using System;

namespace Cpp2IL.Core.Exceptions;

public class LibraryNotInitializedException : Exception
{
    public override string Message => "This function requires LibCpp2IL to be initialized - there is a function to do this in Cpp2IlApi.";
}
