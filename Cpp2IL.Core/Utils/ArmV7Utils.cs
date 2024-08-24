using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class ArmV7Utils
{
    private static CapstoneArmDisassembler? _armDisassembler;

    private static void InitArmDecompilation()
    {
        var disassembler = CapstoneDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
        disassembler.EnableInstructionDetails = true;
        disassembler.EnableSkipDataMode = true;
        disassembler.DisassembleSyntax = DisassembleSyntax.Intel;
        _armDisassembler = disassembler;
    }

    public static byte[]? TryGetMethodBodyBytesFast(ulong virtAddress, bool isCAGen)
    {
        var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtAddress);

        var length = (startOfNext - virtAddress);
        if (isCAGen && length > 50_000)
            return null;

        if (startOfNext <= 0)
            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            return null;

        var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

        var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtAddress);
        if (rawStartOfNextMethod < rawStart)
            rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

        return LibCpp2IlMain.Binary.GetRawBinaryContent().SubArray((int)rawStart..(int)rawStartOfNextMethod);
    }

    public static List<ArmInstruction> GetArmV7MethodBodyAtVirtualAddress(ulong virtAddress, bool managed = true, int count = -1)
    {
        if (_armDisassembler == null)
            InitArmDecompilation();

        //We can't use CppMethodBodyBytes to get the byte array, because ARMv7 doesn't have filler bytes like x86 does.
        //So we can't work out the end of the method.
        //But we can find the start of the next one! (If managed)
        if (managed)
        {
            var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtAddress);

            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            if (startOfNext > 0)
            {
                var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

                var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtAddress);
                if (rawStartOfNextMethod < rawStart)
                    rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

                byte[] bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().SubArray((int)rawStart..(int)rawStartOfNextMethod);

                var iter = _armDisassembler!.Iterate(bytes, (long)virtAddress);
                if (count > 0)
                    iter = iter.Take(count);

                return iter.ToList();
            }
        }

        //Unmanaged function, look for first b or bl
        var pos = (int)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(virtAddress);
        var allBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();
        List<ArmInstruction> ret = [];

        while (!ret.Any(i => i.Mnemonic is "b" or ".byte") && (count == -1 || ret.Count < count))
        {
            //All arm64 instructions are 4 bytes
            ret.AddRange(_armDisassembler!.Iterate(allBytes.SubArray(pos..(pos + 4)), (long)virtAddress));
            virtAddress += 4;
            pos += 4;
        }

        return ret;
    }
}
