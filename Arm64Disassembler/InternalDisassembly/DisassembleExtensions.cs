using System.Runtime.CompilerServices;

namespace Arm64Disassembler.InternalDisassembly;

internal static class DisassembleExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TestBit(this uint instruction, int bit) => (instruction & (1u << bit)) != 0;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TestPattern(this uint original, int maskAndPattern) => (original & maskAndPattern) == maskAndPattern;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TestPattern(this uint original, int mask, int pattern) => (original & mask) == pattern;
}
