namespace Arm64Disassembler.InternalDisassembly;

public static class Arm64Sve
{
    public static Arm64Instruction Disassemble(uint instruction)
    {
        var op0 = (instruction >> 29) & 0b111; //Bits 29-31
        var op1 = (instruction >> 23) & 0b11; //Bits 23-24
        var op2 = (instruction >> 17) & 0b1_1111; //Bits 17-21
        var op3 = (instruction >> 10) & 0b11_1111; //Bits 10-15
        var op4 = (instruction >> 4) & 1; //Bit 4
        
        //TODO
        
        throw new NotImplementedException();
    }
}