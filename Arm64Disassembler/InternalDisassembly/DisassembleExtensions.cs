namespace Arm64Disassembler.InternalDisassembly;

internal static class DisassembleExtensions
{
    public static bool TestBit(this uint instruction, int bit) => (instruction & (1u << bit)) != 0;
    
    public static bool TestPattern(this uint original, int maskAndPattern) => (original & maskAndPattern) == maskAndPattern;
    public static bool TestPattern(this uint original, int mask, int pattern) => (original & mask) == pattern;
}
