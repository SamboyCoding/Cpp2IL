using System;

namespace Cpp2IL.Core.Exceptions;

public class UnsupportedInstructionSetException(string? instructionSetId = null) : Exception
{
    public override string Message => $"This action is not supported on the {instructionSetId ?? "unknown"} instruction set yet. If running the CLI, try adding the --skip-analysis argument.";
}
