namespace Arm64Disassembler;

public static class Arm64DataProcessingImmediate
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        //This one, at least, is mercifully simple
        var op0 = (instruction >> 23) & 0b111; //Bits 23-25
        
        //All 8 possible variants are defined, as 7 subcategories.
        return op0 switch
        {
            0b000 or 0b001 => PcRelativeAddressing(instruction),
            0b010 => AddSubtractImmediate(instruction),
            0b011 => AddSubtractImmediateWithTags(instruction),
            0b100 => LogicalImmediate(instruction),
            0b101 => MoveWideImmediate(instruction),
            0b110 => Bitfield(instruction),
            0b111 => Extract(instruction),
            _ => throw new ArgumentOutOfRangeException(nameof(op0), "Impossible op0 value")
        };
    }

    public static Arm64Instruction PcRelativeAddressing(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction AddSubtractImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction AddSubtractImmediateWithTags(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction LogicalImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction MoveWideImmediate(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction Bitfield(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction Extract(uint instruction)
    {
        throw new NotImplementedException();
    }
}