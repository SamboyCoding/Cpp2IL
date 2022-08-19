namespace Arm64Disassembler;

public static class Arm64DataProcessingRegister
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = (instruction >> 30) & 1; //Bit 30
        var op1 = (instruction >> 28) & 1; //Bit 28
        //25-27 must be 101
        var op2 = (instruction >> 21) & 0b1111; //Bits 21-24
        var op3 = (instruction >> 10) & 0b11_1111; //Bits 10-15
        
        //TODO
        
        throw new NotImplementedException();
    }
}