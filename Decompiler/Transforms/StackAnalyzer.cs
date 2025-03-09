using System.Diagnostics;
using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Analyzes the stack and replaces it with registers.
/// Taken from https://github.com/SamboyCoding/Cpp2IL/blob/development-gompo-ast/Cpp2IL.Core/Graphs/Analysis/Stack/StackAnalyzer.cs
/// </summary>
public class StackAnalyzer : ITransform
{
    [DebuggerDisplay("Size = {Size}")]
    private class StackEntry
    {
        public int Size;
        public StackEntry Copy() => new() { Size = this.Size };
    }

    private HashSet<Block> _visited = [];
    private Dictionary<Block, StackEntry> _inComingDelta = [];
    private Dictionary<Block, StackEntry> _outGoingDelta = [];
    private Dictionary<Instruction, StackEntry> _instructionState = [];

    public void Apply(Method method)
    {
        _visited.Clear();
        _inComingDelta.Clear();
        _outGoingDelta.Clear();
        _instructionState.Clear();

        var graph = method.ControlFlowGraph;

        _inComingDelta = new Dictionary<Block, StackEntry> { { graph.EntryBlock, new StackEntry() } };

        TraverseGraph(graph.EntryBlock, graph, method);

        var outDelta = _outGoingDelta[graph.ExitBlock];
        if (outDelta.Size != 0)
        {
            var outText = outDelta.Size < 0 ? "-" + (-outDelta.Size).ToString("X") : outDelta.Size.ToString("X");
            method.AddWarning($"Method ends with non empty stack, the output will probably be wrong! ({outText})");
        }

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
                    if (previous is { OpCode: OpCode.Move })
                    {
                        var operandIndex = 0;

                        if (previous.Operands[0] is StackOffsetOperand offset)
                            operandIndex = 0;
                        if (previous.Operands[1] is StackOffsetOperand offset2)
                            operandIndex = 1;

                        var actualOffset = _instructionState[instruction].Size;
                        previous.Operands[operandIndex] = new StackOffsetOperand(actualOffset);
                    }
                }

                previous = instruction;
            }

            if (!block.IsCall) continue;

            // Correct offsets for call params
            var callInstruction = block.Instructions.Last();
            var stackSize = _instructionState[callInstruction].Size;

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

        graph.MergeCallBlocks();
        graph.RemoveNops();
    }

    // Traverse the graph and calculate the stack state for each block and instruction
    private void TraverseGraph(Block block, ControlFlowGraph graph, Method method)
    {
        var blockDelta = _inComingDelta[block].Copy();

        var previous = blockDelta;

        foreach (var instruction in block.Instructions)
        {
            _instructionState[instruction] = previous;

            if (instruction.OpCode == OpCode.ShiftStack)
            {
                var offset = ((IntOperand)instruction.Operands[0]!).Value;

                previous = previous.Copy();

                // Change stack state
                previous.Size += offset;
            }
            else if (instruction.OpCode == OpCode.TailCall)
            {
                previous = previous.Copy();

                // Tail calls clear stack
                previous.Size = 0;
            }
        }

        blockDelta = previous;

        // Tail calls clear stack
        if (block.IsTailCall)
            blockDelta.Size = 0;

        _outGoingDelta[block] = blockDelta;

        // Traverse successors
        foreach (var successor in block.Successors)
        {
            if (!_visited.Contains(successor))
            {
                _inComingDelta[successor] = blockDelta;
                _visited.Add(successor);
                TraverseGraph(successor, graph, method);
            }
            else
            {
                var expectedDelta = _inComingDelta[successor];

                if (expectedDelta.Size != blockDelta.Size)
                {
                    var expectedText = expectedDelta.Size < 0 ? "-" + (-expectedDelta.Size).ToString("X") : expectedDelta.Size.ToString("X");
                    var actualText = blockDelta.Size < 0 ? "-" + (-blockDelta.Size).ToString("X") : blockDelta.Size.ToString("X");
                    method.AddWarning($"Unbalanced stack, the output will probably be wrong! expected: {expectedText}, actual: {actualText}");
                }

                _inComingDelta[successor] = blockDelta;
            }
        }
    }
}
