namespace Cpp2IL.Core.ISIL;

public class IsilCondition(
    InstructionSetIndependentOperand left,
    InstructionSetIndependentOperand right,
    InstructionSetIndependentOpCode opCode)
{
    public InstructionSetIndependentOperand Left = left;
    public InstructionSetIndependentOperand Right = right;
    public InstructionSetIndependentOpCode OpCode = opCode;

    public bool IsAnd; //E.g. x86 TEST instruction vs CMP

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
