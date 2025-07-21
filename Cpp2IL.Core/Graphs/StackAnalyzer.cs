using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Graphs;

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

    private const int MaxBlockVisitCount = 5000;

    private StackAnalyzer()
    {
    }

    public static void Analyze(MethodAnalysisContext method)
    {
        var analyzer = new StackAnalyzer();

        var graph = method.ControlFlowGraph;

        analyzer._inComingState = new Dictionary<Block, StackState> { { graph!.EntryBlock, new StackState() } };
        analyzer.TraverseGraph(graph.EntryBlock);

        var outDelta = analyzer._outGoingState[graph.ExitBlock];
        if (outDelta.Size != 0)
        {
            var outText = outDelta.Size < 0 ? "-" + (-outDelta.Size).ToString("X") : outDelta.Size.ToString("X");
            method.AnalysisWarnings.Add($"warning: method ends with non empty stack: {outText}");
            Logger.Warn($"Method {method.FullName} ends with non empty stack: {outText}", "StackAnalyzer");
        }

        analyzer.CorrectOffsets(graph);
        ReplaceStackWithRegisters(method);
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

                // Correct offset for stack operands
                for (var i = 0; i < instruction.Operands.Count; i++)
                {
                    var op = instruction.Operands[i];

                    if (op is StackOffset offset)
                    {
                        // TODO: sometimes try catch causes something weird, probably indirect jump somewhere, so some instructions are in cfg but not in _instructionState
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
        for (var i = 0; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];

            _instructionState[instruction] = currentState;

            if (instruction.OpCode == OpCode.ShiftStack)
            {
                var offset = (int)instruction.Operands[0];
                currentState = currentState.Copy();
                currentState.Size += offset;
            }
            else if (i == block.Instructions.Count - 1 && block.BlockType == BlockType.TailCall)
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
            throw new Exception($"Stack state not settling ({MaxBlockVisitCount} blocks already visited)");

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
        // Get all offsets without duplicates
        var offsets = new List<int>();
        foreach (var operand in method.ConvertedIsil!.SelectMany(instruction => instruction.Operands))
        {
            if (operand is StackOffset offset)
            {
                if (!offsets.Contains(offset.Offset))
                    offsets.Add(offset.Offset);
            }
        }

        // Map offsets to registers
        var offsetToRegister = new Dictionary<int, string>();
        foreach (var offset in offsets)
        {
            var name = offset < 0 ? $"stack_-{-offset:X}" : $"stack_{offset:X}";
            offsetToRegister.Add(offset, name);
        }

        // Replace stack offset operands
        foreach (var instruction in method.ConvertedIsil!)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is StackOffset offset)
                    instruction.Operands[i] =
                        new Register(null, offsetToRegister[offset.Offset]);
            }
        }
    }
}
