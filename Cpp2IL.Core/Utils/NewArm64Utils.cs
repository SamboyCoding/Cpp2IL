using System;
using System.Linq;
using Arm64Disassembler;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class NewArm64Utils
{
    public static Arm64DisassemblyResult GetArm64MethodBodyAtVirtualAddress(ulong virtAddress, bool managed = true, int count = -1)
    {
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

                var bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().AsSpan((int)rawStart, (int)(rawStartOfNextMethod - rawStart));

                return Disassembler.Disassemble(bytes, virtAddress);
            }
        }

        //Unmanaged function, look for first b
        var pos = (int) LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(virtAddress);
        var allBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();
        var span = allBytes.AsSpan(pos, 4);
        Arm64DisassemblyResult ret = new();

        try
        {
            while ((count == -1 || ret.Instructions.Count < count) && !ret.Instructions.Any(i => i.Mnemonic is Arm64Mnemonic.B))
            {
                ret = Disassembler.Disassemble(span, virtAddress);

                //All arm64 instructions are 4 bytes
                span = allBytes.AsSpan(pos, span.Length + 4);
            }
        }
        catch (Exception e)
        {
            throw new($"Failed to disassemble method body: {string.Join(", ", span.ToArray().Select(b => "0x" + b.ToString("X2")))}", e);
        }

        return ret;
    } 
}