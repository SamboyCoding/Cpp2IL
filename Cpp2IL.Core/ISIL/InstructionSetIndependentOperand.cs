using System;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public readonly struct InstructionSetIndependentOperand
{
    public readonly OperandType Type;
    public readonly IsilOperandData Data;

    public static InstructionSetIndependentOperand MakeRegister(string registerName) => new(OperandType.Register, new IsilRegisterOperand(registerName));
    public static InstructionSetIndependentOperand MakeMemory(IsilMemoryOperand memory) => new(OperandType.Memory, memory);
    public static InstructionSetIndependentOperand MakeImmediate(IConvertible value) => new(OperandType.Immediate, new IsilImmediateOperand(value));
    public static InstructionSetIndependentOperand MakeStack(int value) => new(OperandType.StackOffset, new IsilStackOperand(value));
    public static InstructionSetIndependentOperand MakeInstruction(InstructionSetIndependentInstruction instruction) => new(OperandType.Instruction, instruction);
    public static InstructionSetIndependentOperand MakeVectorElement(string registerName, IsilVectorRegisterElementOperand.VectorElementWidth width, int index) => new(OperandType.Register, new IsilVectorRegisterElementOperand(registerName, width, index));
    public static InstructionSetIndependentOperand MakeTypeMetadataUsage(TypeAnalysisContext value) => new(OperandType.TypeMetadataUsage, new IsilTypeMetadataUsageOperand(value));
    public static InstructionSetIndependentOperand MakeMethodReference(MethodAnalysisContext value) => new(OperandType.MethodReference, new IsilMethodOperand(value));


    private InstructionSetIndependentOperand(OperandType type, IsilOperandData data)
    {
        Type = type;
        Data = data;
    }

    public override string? ToString()
    {
        if (Data is InstructionSetIndependentInstruction instruction)
            return $"{{{instruction.InstructionIndex.ToString()}}}"; //Special case for instructions, we want to show the index in braces. Otherwise we print the entire instruction and it looks weird.

        return Data.ToString();
    }

    [Flags]
    public enum OperandType
    {
        Immediate = 1,
        StackOffset = 2,
        Register = 4,
        Memory = 8,
        Instruction = 16,
        TypeMetadataUsage = 32,
        MethodReference = 64,

        MemoryOrStack = Memory | StackOffset,
        NotStack = Immediate | Register | Memory | Instruction | TypeMetadataUsage | MethodReference,


        Any = Immediate | StackOffset | Register | Memory | TypeMetadataUsage | MethodReference
    }
}
