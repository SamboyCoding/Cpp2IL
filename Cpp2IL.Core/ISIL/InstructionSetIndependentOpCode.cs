using System;
using System.Linq;
using Cpp2IL.Core.Extensions;

namespace Cpp2IL.Core.ISIL;

public class InstructionSetIndependentOpCode
{
    public static readonly InstructionSetIndependentOpCode Move = new(IsilMnemonic.Move, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode LoadAddress = new(IsilMnemonic.LoadAddress, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.MemoryOrStack);
    public static readonly InstructionSetIndependentOpCode Call = new(IsilMnemonic.Call);
    public static readonly InstructionSetIndependentOpCode CallNoReturn = new(IsilMnemonic.CallNoReturn);
    public static readonly InstructionSetIndependentOpCode Add = new(IsilMnemonic.Add, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Subtract = new(IsilMnemonic.Subtract, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Multiply = new(IsilMnemonic.Multiply, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Divide = new(IsilMnemonic.Divide, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode ShiftLeft = new(IsilMnemonic.ShiftLeft, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode ShiftRight = new(IsilMnemonic.ShiftRight, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode And = new(IsilMnemonic.And, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Or = new(IsilMnemonic.Or, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Xor = new(IsilMnemonic.Xor, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Not = new(IsilMnemonic.Not, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode CompareEqual = new(IsilMnemonic.CompareEqual, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Memory);
    public static readonly InstructionSetIndependentOpCode CompareNotEqual = new(IsilMnemonic.CompareNotEqual, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Memory);
    public static readonly InstructionSetIndependentOpCode CompareLessThan = new(IsilMnemonic.CompareLessThan, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Memory);
    public static readonly InstructionSetIndependentOpCode CompareGreaterThan = new(IsilMnemonic.CompareGreaterThan, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Memory);
    public static readonly InstructionSetIndependentOpCode CompareLessThanOrEqual = new(IsilMnemonic.CompareLessThanOrEqual, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Memory);
    public static readonly InstructionSetIndependentOpCode CompareGreaterThanOrEqual = new(IsilMnemonic.CompareGreaterThanOrEqual, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Memory);
    public static readonly InstructionSetIndependentOpCode ShiftStack = new(IsilMnemonic.ShiftStack, 1, InstructionSetIndependentOperand.OperandType.Immediate);
    public static readonly InstructionSetIndependentOpCode Return = new(IsilMnemonic.Return, 1, InstructionSetIndependentOperand.OperandType.NotStack);

    public readonly IsilMnemonic Mnemonic;
    public readonly InstructionSetIndependentOperand.OperandType[] PermittedOperandTypes;
    public readonly int MaxOperands;

    public InstructionSetIndependentOpCode(IsilMnemonic mnemonic)
    {
        Mnemonic = mnemonic;
        MaxOperands = int.MaxValue;
        PermittedOperandTypes = Array.Empty<InstructionSetIndependentOperand.OperandType>();
    }

    public InstructionSetIndependentOpCode(IsilMnemonic mnemonic, int maxOperands)
    {
        Mnemonic = mnemonic;
        MaxOperands = maxOperands;
        PermittedOperandTypes = InstructionSetIndependentOperand.OperandType.Any.Repeat(maxOperands).ToArray();
    }

    private InstructionSetIndependentOpCode(IsilMnemonic mnemonic, int maxOperands, params InstructionSetIndependentOperand.OperandType[] permittedOperandTypes)
    {
        Mnemonic = mnemonic;
        PermittedOperandTypes = permittedOperandTypes;
        MaxOperands = maxOperands;
    }
    
    public void Validate(InstructionSetIndependentInstruction instruction)
    {
        var operands = instruction.Operands;
        
        if (operands.Length > MaxOperands)
            throw new($"Too many operands! We have {operands.Length} but we only allow {MaxOperands}");

        if (PermittedOperandTypes.Length == 0)
            return;
        
        for (var i = 0; i < operands.Length; i++)
        {
            if ((operands[i].Type & PermittedOperandTypes[i]) == 0)
                throw new($"Instruction {instruction}: Operand {operands[i]} at index {i} (0-based) is of type {operands[i].Type}, which is not permitted for this index of a {Mnemonic} instruction");
        }
    }

    public override string ToString() => Mnemonic.ToString();
}