namespace Arm64Disassembler.InternalDisassembly;

internal static class DisassembleExtensions
{
    public static bool TestBit(this uint instruction, int bit) => (instruction & (1u << bit)) != 0;
}