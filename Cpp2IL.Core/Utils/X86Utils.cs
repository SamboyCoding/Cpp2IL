using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class X86Utils
{
    private static readonly Regex UpscaleRegex = new Regex("(?:^|([^a-zA-Z]))e([a-z]{2})", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, string> CachedUpscaledRegisters = new();
    private static readonly ConcurrentDictionary<Register, string> CachedX86RegNamesNew = new();

    public static unsafe InstructionList Disassemble(ReadOnlySpan<byte> bytes, ulong methodBase, bool is32Bit)
    {
        fixed (byte* ptr = bytes)
        {
            var codeReader = new MemoryCodeReader(ptr, bytes.Length);
            var decoder = Decoder.Create(is32Bit ? 32 : 64, codeReader);
            decoder.IP = methodBase;
            var endRip = decoder.IP + (uint)bytes.Length;

            InstructionList instructions = [];
            while (decoder.IP < endRip)
                instructions.Add(decoder.Decode());

            return instructions;
        }
    }

    public static unsafe InstructionList Iterate(ReadOnlySpan<byte> bytes, ulong methodBase, bool is32Bit)
    {
        fixed (byte* ptr = bytes)
        {
            var codeReader = new MemoryCodeReader(ptr, bytes.Length);
            var decoder = Decoder.Create(is32Bit ? 32 : 64, codeReader);
            decoder.IP = methodBase;

            InstructionList instructions = [];

            decoder.Decode(out var instruction);
            while (!instruction.IsInvalid)
            {
                instructions.Add(instruction);
                decoder.Decode(out instruction);
            }

            return instructions;
        }
    }

    public static IEnumerable<Instruction> Iterate(MethodAnalysisContext context)
    {
        return Iterate(context.RawBytes.AsSpan(), context.UnderlyingPointer, context.AppContext.Binary.is32Bit);
    }

    public static BinarySlice GetRawManagedOrCaCacheGenMethodBody(ulong ptr, bool isCaGen, Il2CppBinary binary)
    {
        var rawAddr = binary.MapVirtualAddressToRaw(ptr, false);

        if (rawAddr <= 0)
            return BinarySlice.Empty;

        var virtStartNextFunc = MiscUtils.GetAddressOfNextFunctionStart(ptr, binary);

        if (virtStartNextFunc == 0 || (isCaGen && virtStartNextFunc - ptr > 50000))
        {
            GetMethodBodyAtVirtAddressNew(ptr, false, binary, out var ret);
            return ret;
        }

        var ra2 = binary.MapVirtualAddressToRaw(virtStartNextFunc, false);

        if (ra2 <= 0)
        {
            //Don't have a known end point => fall back
            GetMethodBodyAtVirtAddressNew(ptr, false, binary, out var ret);
            return ret;
        }

        var startOfNextFunc = (int)ra2;

        if (startOfNextFunc < rawAddr)
        {
            Logger.WarnNewline($"StartOfNextFunc returned va 0x{virtStartNextFunc:X}, raw address 0x{startOfNextFunc:X}, for raw address 0x{rawAddr:X}. It should be more than raw address. Falling back to manual, slow, decompiler-based approach.");
            GetMethodBodyAtVirtAddressNew(ptr, false, binary, out var ret);
            return ret;
        }

        var rawBinary = binary.GetRawBinaryContent();

        var lastPos = startOfNextFunc - 1;

        if (lastPos >= rawBinary.Length)
        {
            Logger.WarnNewline($"StartOfNextFunc returned va 0x{virtStartNextFunc:X}, raw address 0x{startOfNextFunc:X}, for raw address 0x{rawAddr:X}. LastPos should be less than the raw array length. Falling back to manual, slow, decompiler-based approach.");
            GetMethodBodyAtVirtAddressNew(ptr, false, binary, out var ret);
            return ret;
        }

        while (rawBinary[lastPos] == 0xCC && lastPos > rawAddr)
            lastPos--;

        var span = rawBinary.Slice((int)rawAddr, (int)(lastPos - rawAddr + 1));

        if (TryFindJumpTableStart(span, ptr, virtStartNextFunc, out var startIndex, out var jumpTableElements))
        {
            // TODO: Figure out what to do with jumpTableElements, how do we handle returning it from this function?
            // we might need to return the address it was found at in TryFindJumpTableStart function too
            // Should clean up the way we handle the bytes array too
            /*
            foreach (var element in jumpTableElements)
                //Logger.InfoNewline($"Jump table element: 0x{element:x8}.");
            */
            return new BinarySlice(binary, (int)rawAddr, startIndex);
        }

        return new BinarySlice(binary, (int)rawAddr, span.Length);
    }

    private static bool TryFindJumpTableStart(ReadOnlySpan<byte> methodBytes, ulong methodPtr, ulong nextMethodPtr, out int startIndex, out List<ulong> jumpTableElements)
    {
        bool foundTable = false;
        startIndex = 0;
        jumpTableElements = [];
        for (int i = (int)(methodPtr % 4); i < methodBytes.Length; i += 4)
        {
            var result = (ulong)methodBytes.ReadUInt(i);
            var possibleJumpAddress = result + 0x180000000; // image base
            if (possibleJumpAddress > methodPtr && possibleJumpAddress < nextMethodPtr)
            {
                // Sound the alarms, we've more than likely ran into a jump table  
                if (!foundTable)
                {
                    startIndex = i;
                    foundTable = true;
                }

                jumpTableElements.Add(result);
            }
        }

        return foundTable;
    }

    public static InstructionList GetMethodBodyAtVirtAddressNew(ulong addr, bool peek, Il2CppBinary binary) => GetMethodBodyAtVirtAddressNew(addr, peek, binary, out _);

    public static InstructionList GetMethodBodyAtVirtAddressNew(ulong addr, bool peek, Il2CppBinary binary, out BinarySlice rawBytes)
    {
        var ret = new InstructionList();
        var rawAddr = binary.MapVirtualAddressToRaw(addr);

        if (rawAddr < 0 || rawAddr >= binary.RawLength)
        {
            Logger.ErrorNewline($"Invalid call to GetMethodBodyAtVirtAddressNew, virt addr {addr} resolves to raw {rawAddr} which is out of bounds");
            rawBytes = BinarySlice.Empty;
            return ret;
        }

        var functionStart = addr;
        var functionLength = 0;
        var rawBinary = binary.GetRawBinaryContent();
        var startOfNextFunc = MiscUtils.GetAddressOfNextFunctionStart(addr, binary);
        var startOffset = (int)rawAddr;
        var con = true;

        while (con)
        {
            if (addr >= startOfNextFunc)
                break;

            functionLength++;

            ret = Disassemble(rawBinary.Slice(startOffset, functionLength), functionStart, binary.is32Bit);

            if (ret.All(i => i.Mnemonic != Mnemonic.INVALID) && ret.Any(i => i.Code == Code.Int3))
                con = false;

            if (peek && functionLength > 50)
                con = false;
            else if (functionLength > 50000)
                con = false; // Sanity breakout.

            addr++;
            if (startOffset + functionLength >= rawBinary.Length)
                con = false;
        }

        rawBytes = new BinarySlice(binary, startOffset, functionLength);

        return ret;
    }

    public static string UpscaleRegisters(string replaceIn)
    {
        if (CachedUpscaledRegisters.TryGetValue(replaceIn, out var reg))
            return reg;

        if (replaceIn.Length < 2) return replaceIn;

        //Special case the few 8-bit register: "al" => "rax" etc
        if (replaceIn == "al")
            return "rax";
        if (replaceIn == "bl")
            return "rbx";
        if (replaceIn == "dl")
            return "rdx";
        if (replaceIn == "ax")
            return "rax";
        if (replaceIn == "cx" || replaceIn == "cl")
            return "rcx";

        //R9d, etc.
        if (replaceIn[0] == 'r' && replaceIn[^1] == 'd')
            return replaceIn.Substring(0, replaceIn.Length - 1);

        var ret = UpscaleRegex.Replace(replaceIn, "$1r$2");
        CachedUpscaledRegisters.TryAdd(replaceIn, ret);

        return ret;
    }

    public static string GetFloatingRegister(string original)
    {
        switch (original)
        {
            case "rcx":
                return "xmm0";
            case "rdx":
                return "xmm1";
            case "r8":
                return "xmm2";
            case "r9":
                return "xmm3";
            default:
                return original;
        }
    }

    public static string GetRegisterName(Register register)
    {
        if (register == Register.None) return "";

        if (!CachedX86RegNamesNew.TryGetValue(register, out var ret))
        {
            if (!register.IsVectorRegister())
                ret = register.GetFullRegister().ToString().ToLowerInvariant();
            else
                ret = UpscaleRegisters(register.ToString().ToLower());

            CachedX86RegNamesNew[register] = ret;
        }

        return ret;
    }

    private sealed unsafe class MemoryCodeReader(byte* ptr, int length) : CodeReader
    {
        private int _position;

        public override int ReadByte()
        {
            if (_position >= length)
                return -1;

            return ptr[_position++];
        }
    }
}
