using System;

namespace Cpp2IL.Core.Exceptions;

public class UnsupportedInstructionSetException : Exception
{
    private readonly string? _instructionSetId;

    public UnsupportedInstructionSetException()
    {
    }

    public UnsupportedInstructionSetException(string instructionSetId)
    {
        _instructionSetId = instructionSetId;
    }

    public override string Message => $"This action is not supported on the {_instructionSetId ?? "unknown"} instruction set yet. If running the CLI, try adding the --skip-analysis argument.";
}
