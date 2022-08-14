namespace Arm64Disassembler;

public class Arm64UndefinedInstructionException : Exception
{
    public Arm64UndefinedInstructionException(string message) : base(message)
    {
    }
}