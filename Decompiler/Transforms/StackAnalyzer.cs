using System.Diagnostics;
using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Analyzes the stack and removes shift stack instructions.
/// </summary>
public class StackAnalyzer : ITransform
{
    [DebuggerDisplay("Size = {Size}")]
    private class StackState
    {
        public int Size;
        public StackState Copy() => new() { Size = this.Size };
    }

    private Dictionary<Block, StackState> _inComingState = [];
    private Dictionary<Block, StackState> _outGoingState = [];
    private Dictionary<Instruction, StackState> _instructionState = [];

    /// <summary>
    /// Max allowed count of blocks to visit (-1 for no limit).
    /// </summary>
    public int MaxBlockVisitCount = -1;

    public void Apply(Method method)
    {
        var graph = method.ControlFlowGraph;

        _inComingState = new Dictionary<Block, StackState> { { graph.EntryBlock, new StackState() } };
        _outGoingState.Clear();
        _instructionState.Clear();

        TraverseGraph(graph);

        var outDelta = _outGoingState[graph.ExitBlock];
        if (outDelta.Size != 0)
        {
            var outText = outDelta.Size < 0 ? "-" + (-outDelta.Size).ToString("X") : outDelta.Size.ToString("X");
            method.AddWarning($"Method ends with non empty stack ({outText}), the output could be wrong!");
        }

        CorrectOffsets(graph);

        graph.MergeCallBlocks();
        graph.RemoveNops();
    }

    private void CorrectOffsets(ControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is { OpCode: OpCode.ShiftStack })
                {
                    // Nop the shift stack instruction
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];
                }

                // Correct offset for stack operands.
                for (var i = 0; i < instruction.Operands.Count; i++)
                {
                    var op = instruction.Operands[i];
                    if (op == null) continue;

                    if (op.Type != OperandType.StackOffset) continue;

                    var state = _instructionState[instruction].Size;
                    var actual = state + ((StackOffset)op).Offset;
                    instruction.Operands[i] = new StackOffset(actual);
                }
            }
        }
    }

    // Traverse the graph and calculate the stack state for each block and instruction
    private void TraverseGraph(ControlFlowGraph graph)
    {
        var visitedBlockCount = 0;

        var workList = new Queue<Block>();
        workList.Enqueue(graph.EntryBlock);

        while (workList.Count > 0)
        {
            var block = workList.Dequeue();

            // Copy current state
            var incomingState = _inComingState[block];
            var currentState = incomingState.Copy();

            // Process instructions
            foreach (var instruction in block.Instructions)
            {
                _instructionState[instruction] = currentState;

                if (instruction.OpCode == OpCode.ShiftStack)
                {
                    var offset = ((IntOp)instruction.Operands[0]!).Value;
                    currentState = currentState.Copy();
                    currentState.Size += offset;
                }
                else if (instruction.OpCode == OpCode.TailCall)
                {
                    // Tail calls clear stack
                    currentState = currentState.Copy();
                    currentState.Size = 0;
                }
            }

            // Tail calls clear stack
            if (block.IsTailCall)
                currentState.Size = 0;

            _outGoingState[block] = currentState;

            visitedBlockCount++;

            if (MaxBlockVisitCount != -1 && visitedBlockCount > MaxBlockVisitCount)
                throw new Exception($"Too many blocks visited! (max: {MaxBlockVisitCount})");

            // Visit successors
            foreach (var successor in block.Successors)
            {
                // Already visited
                if (_inComingState.TryGetValue(successor, out var existingState))
                {
                    if (existingState.Size != currentState.Size)
                    {
                        _inComingState[successor] = currentState.Copy();
                        workList.Enqueue(successor);
                    }
                }
                else
                {
                    // Set incoming delta and add to queue
                    _inComingState[successor] = currentState.Copy();
                    workList.Enqueue(successor);
                }
            }
        }
    }
}
