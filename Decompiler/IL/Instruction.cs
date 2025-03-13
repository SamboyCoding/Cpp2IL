namespace Decompiler.IL;

/// <summary>
/// A single instruction.
/// </summary>
public class Instruction(int index, OpCode opcode, params IOperand?[] operands) : IOperand
{
    /// <summary>
    /// Index of the instruction.
    /// </summary>
    public int Index = index;

    /// <summary>
    /// Opcode of the instruction.
    /// </summary>
    public OpCode OpCode = opcode;

    /// <summary>
    /// Operands for the instruction.
    /// </summary>
    public List<IOperand?> Operands = operands.ToList();

    /// <summary>
    /// True if the instruction doesn't affect control flow.
    /// </summary>
    public bool IsFallThrough => OpCode != OpCode.Return && OpCode != OpCode.Jump && OpCode != OpCode.ConditionalJump;

    public OperandType Type => OperandType.Instruction;

    /// <summary>
    /// Operands that the instruction reads.
    /// </summary>
    public List<IOperand> ReadOperands
    {
        get
        {
            if (OpCode == OpCode.Move)
                return GetAllReadOperands(Operands[1]);

            var operands = new List<IOperand>();
            foreach (var operand in Operands)
                operands.AddRange(GetAllReadOperands(operand));
            return operands;
        }
    }

    /// <summary>
    /// Operands that the instruction writes to.
    /// </summary>
    public List<IOperand> WrittenOperands
    {
        get
        {
            if (OpCode != OpCode.Move)
                return [];
            return [Operands[0]!];
        }
    }

    private static List<IOperand> GetAllReadOperands(IOperand? operand)
    {
        if (operand == null)
            return [];

        var operands = new List<IOperand>();

        switch (operand.Type)
        {
            case OperandType.Register:
            case OperandType.StackOffset:
                operands.Add(operand);
                break;
            case OperandType.CallInfo:
                if (operand is CallInfo call)
                    operands.AddRange(call.Parameters);
                break;
            case OperandType.Instruction:
                var instruction = (Instruction)operand;
                foreach (var op in instruction.Operands)
                    operands.AddRange(GetAllReadOperands(op!));
                break;
        }

        return operands;
    }

    public override string ToString() =>
        $"({Index} {OpCode} {string.Join(", ", Operands.Select(o => o == null ? "null" : o.ToString()))})";
}
