namespace Cpp2IL.Core.ISIL;

public class IsilCondition
{
    public InstructionSetIndependentOperand Left;
    public InstructionSetIndependentOperand Right;
    public InstructionSetIndependentOpCode OpCode;

    public bool IsAnd; //E.g. x86 TEST instruction vs CMP

    public IsilCondition(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right, InstructionSetIndependentOpCode opCode)
    {
        Left = left;
        Right = right;
        OpCode = opCode;
    }
    
    public IsilCondition MarkAsAnd()
    {
        IsAnd = true;        
        return this;
    }

    public override string ToString()
    {
        return $"{OpCode} {Left},{Right}";
    }
}