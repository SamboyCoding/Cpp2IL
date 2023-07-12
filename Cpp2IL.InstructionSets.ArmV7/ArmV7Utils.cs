using System.Runtime.InteropServices;
using CapstoneSharp.Arm;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.InstructionSets.ArmV7;

internal static class ArmV7Utils
{
    private static CapstoneArmDisassembler? _disassembler;

    // TODO dispose this
    public static CapstoneArmDisassembler Disassembler => _disassembler ??= new CapstoneArmDisassembler
    {
        EnableInstructionDetails = true, EnableSkipData = true,
    };

    public static bool IsAllZero(this ReadOnlySpan<byte> span)
    {
        if (MemoryMarshal.TryRead<int>(span, out var value))
        {
            return value == 0;
        }

        foreach (var b in span)
        {
            if (b != 0)
            {
                return true;
            }
        }

        return true;
    }

    public static int GetBranchTarget(this CapstoneArmInstruction instruction)
    {
        if (instruction.Id is not (CapstoneArmInstructionId.B or CapstoneArmInstructionId.BL))
            throw new InvalidOperationException("Branch target not available for this instruction, must be a B or BL");

        return instruction.Details.ArchDetails.Operands[0].Immediate;
    }

    public static bool IsBranchingTo(this CapstoneArmInstruction instruction, int toWhere)
    {
        if (instruction.Id is not (CapstoneArmInstructionId.B or CapstoneArmInstructionId.BL))
            return false;

        return instruction.GetBranchTarget() == toWhere;
    }

    public static Memory<byte>? TryGetMethodBodyBytesFast(ulong virtualAddress, bool isCAGen)
    {
        var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtualAddress);

        var length = (startOfNext - virtualAddress);
        if (isCAGen && length > 50_000)
            return null;

        if (startOfNext <= 0)
            //We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
            return null;

        var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

        var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtualAddress);
        if (rawStartOfNextMethod < rawStart)
            rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

        return LibCpp2IlMain.Binary.GetRawBinaryContent().AsMemory((int)rawStart, (int)(rawStartOfNextMethod - rawStart));
    }

    public static List<CapstoneArmInstruction> DisassembleFunction(ulong virtualAddress, int count = -1)
    {
        return DisassembleFunction(virtualAddress, out _, count);
    }

    public static List<CapstoneArmInstruction> DisassembleFunction(ulong virtualAddress, out ulong endVirtualAddress, int count = -1)
    {
        // Unmanaged function, look for first b
        var pos = (int)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(virtualAddress);
        var allBytes = LibCpp2IlMain.Binary.GetRawBinaryContent();

        var instructions = new List<CapstoneArmInstruction>();

        endVirtualAddress = virtualAddress;
        foreach (var instruction in Disassembler.Iterate(allBytes.AsSpan(pos), virtualAddress))
        {
            instructions.Add(instruction);
            endVirtualAddress = instruction.Address;
            if (instruction.Id == CapstoneArmInstructionId.B) break;
            if (count != -1 && instructions.Count >= count) break;
        }

        return instructions;
    }

    public static IEnumerable<CapstoneArmInstruction> DisassembleManagedMethod(ulong virtualAddress, int count = -1)
    {
        return DisassembleManagedMethod(virtualAddress, out _, count);
    }

    public static IEnumerable<CapstoneArmInstruction> DisassembleManagedMethod(ulong virtualAddress, out ulong endVirtualAddress, int count = -1)
    {
        var startOfNext = MiscUtils.GetAddressOfNextFunctionStart(virtualAddress);

        // We have to fall through to default behavior for the last method because we cannot accurately pinpoint its end
        if (startOfNext > 0)
        {
            var rawStartOfNextMethod = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(startOfNext);

            var rawStart = LibCpp2IlMain.Binary.MapVirtualAddressToRaw(virtualAddress);
            if (rawStartOfNextMethod < rawStart)
                rawStartOfNextMethod = LibCpp2IlMain.Binary.RawLength;

            var bytes = LibCpp2IlMain.Binary.GetRawBinaryContent().AsMemory((int)rawStart, (int)(rawStartOfNextMethod - rawStart));

            endVirtualAddress = virtualAddress + (ulong)bytes.Length;
            var instructions = Disassembler.Iterate(bytes, virtualAddress);
            return count == -1 ? instructions : instructions.Take(count);
        }

        return DisassembleFunction(virtualAddress, out endVirtualAddress, count);
    }
}
