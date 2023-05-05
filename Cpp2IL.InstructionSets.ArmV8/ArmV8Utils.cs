using Cpp2IL.Core.Utils;
using Disarm;
using LibCpp2IL;

namespace Cpp2IL.InstructionSets.ArmV8;

internal static class ArmV8Utils
{
    public static IEnumerable<Arm64Instruction> GetArm64MethodBodyAtVirtualAddress(ulong virtualAddress, out ulong endVirtualAddress, bool managed = true, int count = -1)
    {
        if (managed)
        {
            var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtualAddress);

            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            if (startOfNext > 0)
            {
                var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

                var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtualAddress);
                if (rawStartOfNextMethod < rawStart)
                    rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

                var bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().AsMemory((int)rawStart, (int)(rawStartOfNextMethod - rawStart));

                return Disassemble(bytes, virtualAddress, out endVirtualAddress);
            }
        }

        //Unmanaged function, look for first b
        var pos = (int)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(virtualAddress);
        var allBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();
        
        var instructions = new List<Arm64Instruction>();

        endVirtualAddress = virtualAddress;
        foreach (var instruction in Disassembler.Disassemble(allBytes.AsSpan(pos), virtualAddress, Disassembler.Options.IgnoreErrors))
        {
            instructions.Add(instruction);
            endVirtualAddress = instruction.Address;
            if (instruction.Mnemonic == Arm64Mnemonic.B) break;
            if (count != -1 && instructions.Count >= count) break;
        }

        return instructions;
    }

    private static IEnumerable<Arm64Instruction> Disassemble(ReadOnlyMemory<byte> bytes, ulong virtualAddress, out ulong endVirtualAddress)
    {
        try
        {
            endVirtualAddress = virtualAddress + (ulong)bytes.Length;
            return Disassembler.Disassemble(bytes, virtualAddress, Disassembler.Options.IgnoreErrors);
        }
        catch (Exception e)
        {
            throw new($"Failed to disassemble method body: {string.Join(", ", bytes.ToArray().Select(b => "0x" + b.ToString("X2")))}", e);
        }
    }
}
