namespace Arm64Disassembler;

public static class Arm64Simd
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = (instruction >> 28) & 0b1111; //Bits 28-31
        //25-27 must be 111
        var op1 = (instruction >> 23) & 0b11; //Bits 23-24
        var op2 = (instruction >> 19) & 0b1111; //Bits 19-22
        var op3 = (instruction >> 10) & 0b1_1111_1111; //Bits 10-18
        
        //TODO
        
        throw new NotImplementedException();
    }

    public static Arm64Instruction LoadStoreSingleStructure(uint instruction)
    {
        throw new NotImplementedException();
    }

    public static Arm64Instruction LoadStoreSingleStructurePostIndexed(uint instruction)
    {
        throw new NotImplementedException();
    }
}