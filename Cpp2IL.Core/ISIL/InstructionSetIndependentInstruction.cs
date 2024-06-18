using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class InstructionSetIndependentInstruction : IsilOperandData
{
    public InstructionSetIndependentOpCode OpCode;
    public InstructionSetIndependentOperand[] Operands;
    public ulong ActualAddress;
    public uint InstructionIndex = 0;
    public IsilFlowControl FlowControl;
    
    public InstructionSetIndependentInstruction(InstructionSetIndependentOpCode opCode, ulong address, IsilFlowControl flowControl, params InstructionSetIndependentOperand[] operands)
    {
        OpCode = opCode;
        Operands = operands;
        ActualAddress = address;
        FlowControl = flowControl;
        OpCode.Validate(this);
    }

    public override string ToString() => $"{InstructionIndex:000} {OpCode} {string.Join(", ", (IEnumerable<InstructionSetIndependentOperand>) Operands)}";

    public void MakeInvalid(string reason)
    {
        OpCode = InstructionSetIndependentOpCode.Invalid;
        Operands = [InstructionSetIndependentOperand.MakeImmediate(reason)];
        FlowControl = IsilFlowControl.Continue;
    }
}
