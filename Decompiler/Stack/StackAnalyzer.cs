using System.Diagnostics;
using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Stack;

/// <summary>
/// Analyzes the stack and replaces it with registers.
/// Taken from https://github.com/SamboyCoding/Cpp2IL/blob/development-gompo-ast/Cpp2IL.Core/Graphs/Analysis/Stack/StackAnalyzer.cs
/// </summary>
public class StackAnalyzer
{
    private class StackEntry
    {
        public int Size;
        public void Push() => Size++;
        public void Pop() => Size--;
        public StackEntry Copy() => new() { Size = this.Size };
    }

    private HashSet<Block> _visited = [];
    private Dictionary<Block, StackEntry> _inComingDelta = [];
    private Dictionary<Instruction, StackEntry> _instructionsState = [];

    private StackAnalyzer() { }

    public static void Analyze(Method method)
    {
        var graph = method.ControlFlowGraph;
        var archSize = method.ArchSize;

        var analyzer = new StackAnalyzer { _inComingDelta = { [graph.EntryBlock] = new StackEntry() } };
        analyzer.TraverseGraph(graph.EntryBlock, archSize, graph);

        foreach (var block in graph.Blocks)
        {
            Instruction? previous = null;

            foreach (var instruction in block.Instructions)
            {
                /*
                 *   Push/Pop
                 *       ShiftStack -operandSize
                 *       Move stack[0], operand
                 */
                if (instruction.OpCode == OpCode.ShiftStack)
                {
                    // Nop the shift stack instruction
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];

                    // Correct stack offset for previous move instruction if it matches (push/pop combo)
                    if (previous is { OpCode: OpCode.Move } && previous.Operands[0] is StackOffsetOperand offset)
                    {
                        if (offset.Offset != 0) continue;

                        var currentPos = (analyzer._instructionsState[instruction].Size) * archSize;
                        previous.Operands[1] = new StackOffsetOperand(currentPos);
                    }
                }

                previous = instruction;
            }

            if (!block.IsCall) continue;

            // Sometimes there are unreachable blocks so this could fail because TraverseGraph only visits successors
            try
            {
                // Correct offsets for call params
                var callInstruction = block.Instructions.Last();

                var stackSize = analyzer._instructionsState[callInstruction].Size * archSize;

                for (var i = 0; i < callInstruction.Operands.Count; i++)
                {
                    var op = callInstruction.Operands[i];
                    if (op == null) continue;

                    if (op.Type == OperandType.StackOffset)
                    {
                        var actual = stackSize - ((StackOffsetOperand)op).Offset;
                        callInstruction.Operands[i] = new StackOffsetOperand(actual);
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        method.RemoveNops();
        method.MergeCallBlocks();

        ReplaceStackWithRegisters(method);
    }

    // Traverse the graph and calculate the stack state for each block and instruction
    private void TraverseGraph(Block block, int archSize, ControlFlowGraph graph)
    {
        var blockDelta = _inComingDelta[block].Copy();

        var previous = blockDelta;

        foreach (var instruction in block.Instructions)
        {
            _instructionsState[instruction] = previous;

            if (instruction.OpCode != OpCode.ShiftStack) continue;

            var value = ((IntOperand)instruction.Operands[0]!).Value;

            previous = previous.Copy();

            // Change stack state
            for (var i = 0; i < Math.Abs(value / archSize); i++)
            {
                if (value < 0)
                    previous.Push();
                else
                    previous.Pop();
            }
        }

        blockDelta = previous;

        // Traverse successors
        foreach (var successor in block.Successors)
        {
            if (!_visited.Contains(successor))
            {
                _inComingDelta[successor] = blockDelta;
                _visited.Add(successor);
                TraverseGraph(successor, archSize, graph);
            }
            else
            {
                _inComingDelta[successor] = blockDelta;
            }
        }
    }

    private static void ReplaceStackWithRegisters(Method method)
    {
        // Get all offsets without duplicates
        var offsets = new List<int>();
        foreach (var operand in method.Instructions.SelectMany(instruction => instruction.Operands))
        {
            if (operand is StackOffsetOperand offset)
            {
                if (!offsets.Contains(offset.Offset))
                    offsets.Add(offset.Offset);
            }
        }

        // Get max register number
        var maxRegisterNumber = 0;
        foreach (var operand in method.Instructions.SelectMany(instruction => instruction.Operands))
        {
            if (operand is RegisterOperand register)
            {
                if (register.Number > maxRegisterNumber)
                    maxRegisterNumber = register.Number;
            }
        }

        // Map offsets to registers
        var offsetToRegister = new Dictionary<int, int>();
        for (var i = 0; i < offsets.Count; i++)
        {
            var offset = offsets[i];
            offsetToRegister.Add(offset, maxRegisterNumber + i + 1);
        }

        // Replace stack offset operands
        foreach (var instruction in method.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is StackOffsetOperand offset)
                    instruction.Operands[i] =
                        new RegisterOperand(offsetToRegister[offset.Offset], $"stack_{offset.Offset:X}");
            }
        }
    }
}
