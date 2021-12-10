namespace Cpp2IL.Core.ISIL;

public class IsilCondition
{
    public InstructionSetIndependentOperand Left;
    public InstructionSetIndependentOperand Right;
    public InstructionSetIndependentOpCode OpCode;

    public IsilCondition(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right, InstructionSetIndependentOpCode opCode)
    {
        Left = left;
        Right = right;
        OpCode = opCode;
    }
}