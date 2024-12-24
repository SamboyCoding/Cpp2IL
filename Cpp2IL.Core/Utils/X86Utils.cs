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

    //TODO Consider implementing a CodeReader for Memory
    public static InstructionList Disassemble(Memory<byte> bytes, ulong methodBase)
        => Disassemble(bytes.ToArray(), methodBase);

    public static InstructionList Disassemble(byte[] bytes, ulong methodBase)
    {
        var codeReader = new ByteArrayCodeReader(bytes);
        var decoder = Decoder.Create(LibCpp2IlMain.Binary!.is32Bit ? 32 : 64, codeReader);
        decoder.IP = methodBase;
        var instructions = new InstructionList();
        var endRip = decoder.IP + (uint)bytes.Length;

        while (decoder.IP < endRip)
            instructions.Add(decoder.Decode());

        return instructions;
    }

    public static InstructionList Disassemble(MethodAnalysisContext context)
    {
        return Disassemble(context.RawBytes, context.UnderlyingPointer);
    }

    public static IEnumerable<Instruction> Iterate(Memory<byte> bytes, ulong methodBase)
    {
        return Iterate(bytes.AsEnumerable(), methodBase);
    }

    public static IEnumerable<Instruction> Iterate(IEnumerable<byte> bytes, ulong methodBase)
    {
        var codeReader = new EnumerableCodeReader(bytes);
        var decoder = Decoder.Create(LibCpp2IlMain.Binary!.is32Bit ? 32 : 64, codeReader);
        decoder.IP = methodBase;

        decoder.Decode(out var instruction);
        while (!instruction.IsInvalid)
        {
            yield return instruction;
            decoder.Decode(out instruction);
        }
    }

    public static IEnumerable<Instruction> Iterate(MethodAnalysisContext context)
    {
        return Iterate(context.RawBytes, context.UnderlyingPointer);
    }

    public static Memory<byte> GetRawManagedOrCaCacheGenMethodBody(ulong ptr, bool isCaGen)
    {
        var rawAddr = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(ptr, false);

        if (rawAddr <= 0)
            return Memory<byte>.Empty;

        var virtStartNextFunc = MiscUtils.GetAddressOfNextFunctionStart(ptr);

        if (virtStartNextFunc == 0 || (isCaGen && virtStartNextFunc - ptr > 50000))
        {
            GetMethodBodyAtVirtAddressNew(ptr, false, out var ret);
            return ret;
        }

        var ra2 = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtStartNextFunc, false);

        if (ra2 <= 0)
        {
            //Don't have a known end point => fall back
            GetMethodBodyAtVirtAddressNew(ptr, false, out var ret);
            return ret;
        }

        var startOfNextFunc = (int)ra2;

        if (startOfNextFunc < rawAddr)
        {
            Logger.WarnNewline($"StartOfNextFunc returned va 0x{virtStartNextFunc:X}, raw address 0x{startOfNextFunc:X}, for raw address 0x{rawAddr:X}. It should be more than raw address. Falling back to manual, slow, decompiler-based approach.");
            GetMethodBodyAtVirtAddressNew(ptr, false, out var ret);
            return ret;
        }

        var rawArray = LibCpp2IlMain.Binary.GetRawBinaryContent();

        var lastPos = startOfNextFunc - 1;

        if (lastPos >= rawArray.Length)
        {
            Logger.WarnNewline($"StartOfNextFunc returned va 0x{virtStartNextFunc:X}, raw address 0x{startOfNextFunc:X}, for raw address 0x{rawAddr:X}. LastPos should be less than the raw array length. Falling back to manual, slow, decompiler-based approach.");
            GetMethodBodyAtVirtAddressNew(ptr, false, out var ret);
            return ret;
        }

        while (rawArray[lastPos] == 0xCC && lastPos > rawAddr)
            lastPos--;
        var memArray = rawArray.AsMemory((int)rawAddr, (int)(lastPos - rawAddr + 1));

        if (TryFindJumpTableStart(memArray, ptr, virtStartNextFunc, out var startIndex, out var jumpTableElements))
        {
            // TODO: Figure out what to do with jumpTableElements, how do we handle returning it from this function?
            // we might need to return the address it was found at in TryFindJumpTableStart function too 
            // Should clean up the way we handle the bytes array too
            /*
            foreach (var element in jumpTableElements)
                //Logger.InfoNewline($"Jump table element: 0x{element:x8}.");
            */
            memArray = memArray.Slice(0, startIndex);
        }

        return memArray;
    }

    private static bool TryFindJumpTableStart(Memory<byte> methodBytes, ulong methodPtr, ulong nextMethodPtr, out int startIndex, out List<ulong> jumpTableElements)
    {
        bool foundTable = false;
        startIndex = 0;
        jumpTableElements = [];
        for (int i = (int)(methodPtr % 4); i < methodBytes.Length; i += 4)
        {
            var result = (ulong)methodBytes.Span.ReadUInt(i);
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

    public static InstructionList GetMethodBodyAtVirtAddressNew(ulong addr, bool peek) => GetMethodBodyAtVirtAddressNew(addr, peek, out _);

    public static InstructionList GetMethodBodyAtVirtAddressNew(ulong addr, bool peek, out byte[] rawBytes)
    {
        var functionStart = addr;
        var ret = new InstructionList();
        var con = true;
        var buff = new List<byte>();
        var rawAddr = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(addr);
        var startOfNextFunc = MiscUtils.GetAddressOfNextFunctionStart(addr);

        if (rawAddr < 0 || rawAddr >= LibCpp2IlMain.Binary.RawLength)
        {
            Logger.ErrorNewline($"Invalid call to GetMethodBodyAtVirtAddressNew, virt addr {addr} resolves to raw {rawAddr} which is out of bounds");
            rawBytes = [];
            return ret;
        }

        while (con)
        {
            if (addr >= startOfNextFunc)
                break;

            buff.Add(LibCpp2IlMain.Binary.GetByteAtRawAddress((ulong)rawAddr));

            ret = X86Utils.Disassemble(buff.ToArray(), functionStart);

            if (ret.All(i => i.Mnemonic != Mnemonic.INVALID) && ret.Any(i => i.Code == Code.Int3))
                con = false;

            if (peek && buff.Count > 50)
                con = false;
            else if (buff.Count > 50000)
                con = false; //Sanity breakout.

            addr++;
            rawAddr++;
            if (rawAddr >= LibCpp2IlMain.Binary.RawLength)
                con = false;
        }

        rawBytes = buff.ToArray();

        return ret;
    }

    public static string UpscaleRegisters(string replaceIn)
    {
        if (CachedUpscaledRegisters.ContainsKey(replaceIn))
            return CachedUpscaledRegisters[replaceIn];

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

    private class EnumerableCodeReader(IEnumerable<byte> bytes) : CodeReader
    {
        private readonly IEnumerator<byte> _enumerator = bytes.GetEnumerator();

        public override int ReadByte()
        {
            if (_enumerator.MoveNext())
                return _enumerator.Current;

            return -1;
        }
    }
}
