using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

public class StackAnalyzer
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
    public static int MaxBlockVisitCount = 2000;

    public static void Analyze(MethodAnalysisContext method)
    {
        var analyzer = new StackAnalyzer();

        var graph = method.ControlFlowGraph!;
        graph.RemoveUnreachableBlocks(); // Without this indirect jumps (in try catch i think) cause some weird stuff

        analyzer._inComingState = new Dictionary<Block, StackState> { { graph.EntryBlock, new StackState() } };

        analyzer.TraverseGraph(graph.EntryBlock);

        var outDelta = analyzer._outGoingState[graph.ExitBlock];
        if (outDelta.Size != 0)
        {
            var outText = outDelta.Size < 0 ? "-" + (-outDelta.Size).ToString("X") : outDelta.Size.ToString("X");
            method.AddWarning($"Method ends with non empty stack ({outText}), the output could be wrong!");
        }

        analyzer.CorrectOffsets(graph);
        ReplaceStackWithRegisters(method);

        graph.RemoveNops();
        graph.RemoveEmptyBlocks();
    }

    private void CorrectOffsets(ISILControlFlowGraph graph)
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
            else if (block.Instructions[block.Instructions.Count - 1] == instruction && block.BlockType == BlockType.TailCall)
            {
                // Tail calls clear stack
                currentState = currentState.Copy();
                currentState.Size = 0;
            }
        }

        // Tail calls clear stack
        if (block.BlockType == BlockType.TailCall)
            currentState.Size = 0;

        _outGoingState[block] = currentState;

        visitedBlockCount++;

        if (MaxBlockVisitCount != -1 && visitedBlockCount > MaxBlockVisitCount)
            throw new DecompilerException($"Stack state not settling! ({MaxBlockVisitCount} blocks already visited)");

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

    private static void ReplaceStackWithRegisters(MethodAnalysisContext method)
    {
        var instructions = method.ControlFlowGraph!.Instructions;

        // Replace stack offset operands
        foreach (var instruction in instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is StackOffset offset)
                {
                    var name = offset.Offset < 0 ? $"stack_-{-offset.Offset:X}" : $"stack_{offset.Offset:X}";
                    instruction.Operands[i] = new Register(null, name);
                }
            }
        }

        // Replace params
        for (var i = 0; i < method.ParameterOperands.Count; i++)
        {
            var parameter = method.ParameterOperands[i];

            if (parameter is StackOffset offset)
            {
                var name = offset.Offset < 0 ? $"stack_-{-offset.Offset:X}" : $"stack_{offset.Offset:X}";
                method.ParameterOperands[i] = new Register(null, name);
            }
        }
    }
}
