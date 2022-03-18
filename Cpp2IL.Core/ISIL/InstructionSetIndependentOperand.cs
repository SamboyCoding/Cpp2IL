using System;

namespace Cpp2IL.Core.ISIL;

public readonly struct InstructionSetIndependentOperand
{
    public readonly OperandType Type;
    public readonly IsilOperandData Data;
    
    public static InstructionSetIndependentOperand MakeRegister(string registerName) => new(OperandType.Register, new IsilRegisterOperand(registerName));
    public static InstructionSetIndependentOperand MakeMemory(IsilMemoryOperand memory) => new(OperandType.Memory, memory);
    public static InstructionSetIndependentOperand MakeImmediate(IConvertible value) => new(OperandType.Immediate, new IsilImmediateOperand(value));
    public static InstructionSetIndependentOperand MakeStack(int value) => new(OperandType.StackOffset, new IsilStackOperand(value));

    private InstructionSetIndependentOperand(OperandType type, IsilOperandData data)
    {
        Type = type;
        Data = data;
    }

    public override string ToString() => Data.ToString();

    [Flags]
    public enum OperandType
    {
        Immediate = 1,
        StackOffset = 2,
        Register = 4,
        Memory = 8,
        
        MemoryOrStack = Memory | StackOffset,
        NotStack = Immediate | Register | Memory,
        Any = Immediate | StackOffset | Register | Memory
    }
}