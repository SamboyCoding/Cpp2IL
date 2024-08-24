using System;
using LibCpp2IL;

namespace Cpp2IL.Core.Exceptions;

public class UnsupportedInstructionSetException : Exception
{
    public override string Message => $"This action is not supported on the {LibCpp2IlMain.Binary?.InstructionSetId} instruction set yet. If running the CLI, try adding the --skip-analysis argument.";
}
