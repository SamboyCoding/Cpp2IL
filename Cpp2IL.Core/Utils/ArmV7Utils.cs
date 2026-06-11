using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class ArmV7Utils
{
    private static CapstoneArmDisassembler? _armDisassembler;

    [MemberNotNull(nameof(_armDisassembler))]
    private static void InitArmDecompilation()
    {
        var disassembler = CapstoneDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
        disassembler.EnableInstructionDetails = true;
        disassembler.EnableSkipDataMode = true;
        disassembler.DisassembleSyntax = DisassembleSyntax.Intel;
        _armDisassembler = disassembler;
    }

    public static BinarySlice TryGetMethodBodyBytesFast(Il2CppBinary binary, ulong virtAddress, bool isCAGen)
    {
        var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtAddress, binary);

        var length = (startOfNext - virtAddress);
        if (isCAGen && length > 50_000)
            return BinarySlice.Empty;

        if (startOfNext <= 0)
            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            return BinarySlice.Empty;

        var rawStartOfNextMethod = binary.MapVirtualAddressToRaw(startOfNext);

        var rawStart = binary.MapVirtualAddressToRaw(virtAddress);
        if (rawStartOfNextMethod < rawStart)
            rawStartOfNextMethod = binary.RawLength;

        return new BinarySlice(binary, (int)rawStart, (int)(rawStartOfNextMethod - rawStart));
    }

    public static List<ArmInstruction> GetArmV7MethodBodyAtVirtualAddress(Il2CppBinary binary, ulong virtAddress, bool managed = true, int count = -1)
    {
        if (_armDisassembler == null)
            InitArmDecompilation();

        //We can't use CppMethodBodyBytes to get the byte array, because ARMv7 doesn't have filler bytes like x86 does.
        //So we can't work out the end of the method.
        //But we can find the start of the next one! (If managed)
        if (managed)
        {
            var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtAddress, binary);

            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            if (startOfNext > 0)
            {
                var rawStartOfNextMethod = binary.MapVirtualAddressToRaw(startOfNext);

                var rawStart = binary.MapVirtualAddressToRaw(virtAddress);
                if (rawStartOfNextMethod < rawStart)
                    rawStartOfNextMethod = binary.RawLength;

                var bytes = binary.GetRawBinaryContent()[(int)rawStart..(int)rawStartOfNextMethod];

                var iter = _armDisassembler.Iterate(bytes.ToArray(), (long)virtAddress);
                if (count > 0)
                    iter = iter.Take(count);

                return iter.ToList();
            }
        }

        //Unmanaged function, look for first b or bl
        var pos = (int)binary.MapVirtualAddressToRaw(virtAddress);
        var allBytes = binary.GetRawBinaryContent();
        List<ArmInstruction> ret = [];

        while (!ret.Any(i => i.Mnemonic is "b" or ".byte") && (count == -1 || ret.Count < count))
        {
            //All arm64 instructions are 4 bytes
            ret.AddRange(_armDisassembler.Iterate(allBytes[pos..(pos + 4)].ToArray(), (long)virtAddress));
            virtAddress += 4;
            pos += 4;
        }

        return ret;
    }
}
