using System.Diagnostics;
using Cpp2IL.Decompiler.ControlFlow;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Analyzes the stack and replaces it with registers.
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

    public void Apply(Method method, IContext context)
    {
        var graph = method.ControlFlowGraph;

        _inComingState = new Dictionary<Block, StackState> { { graph.EntryBlock, new StackState() } };
        _outGoingState.Clear();
        _instructionState.Clear();

        TraverseGraph(graph.EntryBlock);

        var outDelta = _outGoingState[graph.ExitBlock];
        if (outDelta.Size != 0)
        {
            var outText = outDelta.Size < 0 ? "-" + (-outDelta.Size).ToString("X") : outDelta.Size.ToString("X");
            method.AddWarning($"Method ends with non empty stack ({outText}), the output could be wrong!");
        }

        CorrectOffsets(graph);
        ReplaceStackWithRegisters(method);

        graph.MergeCallBlocks();
        graph.RemoveNops();
        graph.RemoveEmptyBlocks();
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

                    if (op is StackOffset offset)
                    {
                        var state = _instructionState[instruction].Size;
                        var actual = state + offset.Offset;
                        instruction.Operands[i] = new StackOffset(actual);
                    }
                }
            }
        }
    }

    // Traverse the graph and calculate the stack state for each block and instruction
    private void TraverseGraph(Block block, int visitedBlockCount = 0)
    {
        // Copy current state
        var incomingState = _inComingState[block];
        var currentState = incomingState.Copy();

        // Process instructions
        foreach (var instruction in block.Instructions)
        {
            _instructionState[instruction] = currentState;

            if (instruction.OpCode == OpCode.ShiftStack)
            {
                var offset = (int)instruction.Operands[0];
                currentState = currentState.Copy();
                currentState.Size += offset;
            }
            else if (instruction.IsTailCall)
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
            throw new LimitReachedException($"Stack state not settling! ({MaxBlockVisitCount} blocks already visited)");

        // Visit successors
        foreach (var successor in block.Successors)
        {
            // Already visited
            if (_inComingState.TryGetValue(successor, out var existingState))
            {
                if (existingState.Size != currentState.Size)
                {
                    _inComingState[successor] = currentState.Copy();
                    TraverseGraph(successor, visitedBlockCount + 1);
                }
            }
            else
            {
                // Set incoming delta and add to queue
                _inComingState[successor] = currentState.Copy();
                TraverseGraph(successor, visitedBlockCount + 1);
            }
        }
    }

    private static void ReplaceStackWithRegisters(Method method)
    {
        // Get all offsets without duplicates
        var offsets = new List<int>();
        foreach (var operand in method.Instructions.SelectMany(instruction => instruction.Operands))
        {
            if (operand is StackOffset offset)
            {
                if (!offsets.Contains(offset.Offset))
                    offsets.Add(offset.Offset);
            }
        }

        // Get max register number
        var maxRegisterNumber = 0;
        foreach (var operand in method.Instructions.SelectMany(instruction => instruction.Operands))
        {
            if (operand is Register register)
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

                if (operand is StackOffset offset)
                {
                    var name = offset.Offset < 0 ? $"stack_-{-offset.Offset:X}" : $"stack_{offset.Offset:X}";
                    instruction.Operands[i] = new Register(offsetToRegister[offset.Offset], name);
                }
            }
        }
    }
}
