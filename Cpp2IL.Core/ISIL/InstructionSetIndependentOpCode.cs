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
    public static readonly InstructionSetIndependentOpCode Exchange = new(IsilMnemonic.Exchange, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Add = new(IsilMnemonic.Add, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Subtract = new(IsilMnemonic.Subtract, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Multiply = new(IsilMnemonic.Multiply, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Divide = new(IsilMnemonic.Divide, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode ShiftLeft = new(IsilMnemonic.ShiftLeft, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode ShiftRight = new(IsilMnemonic.ShiftRight, 2, InstructionSetIndependentOperand.OperandType.NotStack, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode And = new(IsilMnemonic.And, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Or = new(IsilMnemonic.Or, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Xor = new(IsilMnemonic.Xor, 3, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Not = new(IsilMnemonic.Not, 1, InstructionSetIndependentOperand.OperandType.NotStack);
    public static readonly InstructionSetIndependentOpCode Compare = new(IsilMnemonic.Compare, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    //public static readonly InstructionSetIndependentOpCode CompareNotEqual = new(IsilMnemonic.CompareNotEqual, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    //public static readonly InstructionSetIndependentOpCode CompareLessThan = new(IsilMnemonic.CompareLessThan, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    //public static readonly InstructionSetIndependentOpCode CompareGreaterThan = new(IsilMnemonic.CompareGreaterThan, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    //public static readonly InstructionSetIndependentOpCode CompareLessThanOrEqual = new(IsilMnemonic.CompareLessThanOrEqual, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    //public static readonly InstructionSetIndependentOpCode CompareGreaterThanOrEqual = new(IsilMnemonic.CompareGreaterThanOrEqual, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode ShiftStack = new(IsilMnemonic.ShiftStack, 1, InstructionSetIndependentOperand.OperandType.Immediate);
    public static readonly InstructionSetIndependentOpCode Push = new(IsilMnemonic.Push, 2, InstructionSetIndependentOperand.OperandType.Register, InstructionSetIndependentOperand.OperandType.Any);
    public static readonly InstructionSetIndependentOpCode Pop = new(IsilMnemonic.Pop, 2, InstructionSetIndependentOperand.OperandType.Any, InstructionSetIndependentOperand.OperandType.Register);
    public static readonly InstructionSetIndependentOpCode Return = new(IsilMnemonic.Return, 1, InstructionSetIndependentOperand.OperandType.NotStack);

    public static readonly InstructionSetIndependentOpCode Goto = new(IsilMnemonic.Goto, 1, InstructionSetIndependentOperand.OperandType.Instruction);

    public static readonly InstructionSetIndependentOpCode JumpIfEqual = new(IsilMnemonic.JumpIfEqual, 1, InstructionSetIndependentOperand.OperandType.Instruction);
    public static readonly InstructionSetIndependentOpCode JumpIfNotEqual = new(IsilMnemonic.JumpIfNotEqual, 1, InstructionSetIndependentOperand.OperandType.Instruction);
    public static readonly InstructionSetIndependentOpCode JumpIfGreater = new(IsilMnemonic.JumpIfGreater, 1, InstructionSetIndependentOperand.OperandType.Instruction);
    public static readonly InstructionSetIndependentOpCode JumpIfLess = new(IsilMnemonic.JumpIfLess, 1, InstructionSetIndependentOperand.OperandType.Instruction);
    public static readonly InstructionSetIndependentOpCode JumpIfGreaterOrEqual = new(IsilMnemonic.JumpIfGreaterOrEqual, 1, InstructionSetIndependentOperand.OperandType.Instruction);
    public static readonly InstructionSetIndependentOpCode JumpIfLessOrEqual = new(IsilMnemonic.JumpIfLessOrEqual, 1, InstructionSetIndependentOperand.OperandType.Instruction);

    public static readonly InstructionSetIndependentOpCode Interrupt = new(IsilMnemonic.Interrupt, 0);
    public static readonly InstructionSetIndependentOpCode Nop = new(IsilMnemonic.Nop, 0);

    public static readonly InstructionSetIndependentOpCode NotImplemented = new(IsilMnemonic.NotImplemented, 1, InstructionSetIndependentOperand.OperandType.Immediate);

    public static readonly InstructionSetIndependentOpCode Invalid = new(IsilMnemonic.Invalid, 1, InstructionSetIndependentOperand.OperandType.Immediate);


    public readonly IsilMnemonic Mnemonic;
    public readonly InstructionSetIndependentOperand.OperandType[] PermittedOperandTypes;
    public readonly int MaxOperands;

    public InstructionSetIndependentOpCode(IsilMnemonic mnemonic)
    {
        Mnemonic = mnemonic;
        MaxOperands = int.MaxValue;
        PermittedOperandTypes = [];
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
        {
            instruction.Invalidate($"Too many operands! We have {operands.Length} but we only allow {MaxOperands}");
            return;
        }

        if (PermittedOperandTypes.Length == 0)
            return;

        for (var i = 0; i < operands.Length; i++)
        {
            if ((operands[i].Type & PermittedOperandTypes[i]) == 0)
            {
                instruction.Invalidate($"Operand {operands[i]} at index {i} (0-based) is of type {operands[i].Type}, which is not permitted for this index of a {Mnemonic} instruction");
                return;
            }
        }
    }

    public override string ToString() => Mnemonic.ToString();
}
