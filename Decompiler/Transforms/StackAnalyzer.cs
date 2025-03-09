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
    private Dictionary<Instruction, StackEntry> _instructionsState = [];

    public void Apply(Method method)
    {
        _visited.Clear();
        _inComingDelta.Clear();
        _outGoingDelta.Clear();
        _instructionsState.Clear();

        var graph = method.ControlFlowGraph;

        // If something moves value into stack pointer, try to trace back where that value came from
        foreach (var instruction in method.Instructions)
        {
            if (instruction.OpCode != OpCode.Move) continue;
            if (instruction.Operands[0] is not RegisterOperand { IsStackPointer: true }) continue;

            var src = (RegisterOperand)instruction.Operands[0]!;

            var block = graph.GetBlockByInstruction(instruction)!;
            var value = TraceRegisterValue(block, block.Instructions.Count, src, 0, 25) ?? new IntOperand(0);

            instruction.Operands[1] = value;
        }

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
                    if (previous is { OpCode: OpCode.Move } && previous.Operands[0] is StackOffsetOperand offset)
                    {
                        if (offset.Offset != 0) continue;

                        var currentPos = _instructionsState[instruction].Size;
                        previous.Operands[0] = new StackOffsetOperand(currentPos);
                    }
                }

                previous = instruction;
            }

            if (!block.IsCall) continue;

            // Correct offsets for call params
            var callInstruction = block.Instructions.Last();
            var stackSize = _instructionsState[callInstruction].Size;

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

    private static IOperand? TraceRegisterValue(Block block, int index, IOperand register, int depth, int maxDepth)
    {
        if (depth > maxDepth)
            return null;

        for (var i = index - 1; i >= 0; i--)
        {
            var instruction = block.Instructions[i];

            if (instruction.OpCode == OpCode.Move)
            {
                var dest = instruction.Operands[1];
                var src = instruction.Operands[0];

                if (src != null && dest != null && dest.Equals(register))
                {
                    // Constant
                    if (src.Type is OperandType.Int or OperandType.Long or OperandType.Ulong)
                        return src;

                    // Go back further
                    if (src.Type is OperandType.Register)
                        return TraceRegisterValue(block, i, src, depth + 1, maxDepth);

                    return null;
                }
            }
        }

        // Try to get it from predecessors
        foreach (var pred in block.Predecessors)
        {
            var value = TraceRegisterValue(pred, pred.Instructions.Count, register, depth + 1, maxDepth);
            if (value != null)
                return value;
        }

        return null;
    }


    // Traverse the graph and calculate the stack state for each block and instruction
    private void TraverseGraph(Block block, ControlFlowGraph graph, Method method)
    {
        var blockDelta = _inComingDelta[block].Copy();

        var previous = blockDelta;

        foreach (var instruction in block.Instructions)
        {
            _instructionsState[instruction] = previous;

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
