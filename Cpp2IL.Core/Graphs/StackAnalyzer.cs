using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

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
    private Dictionary<InstructionSetIndependentInstruction, StackState> _instructionState = [];

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
            throw new Exception($"Method {method.FullName} ends with non empty stack: {outText})");
        }

        analyzer.CorrectOffsets(graph);
        ReplaceStackWithRegisters(method);
    }

    private void CorrectOffsets(ISILControlFlowGraph graph)
    {
        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.isilInstructions)
            {
                if (instruction is { OpCode.Mnemonic: IsilMnemonic.ShiftStack })
                {
                    // Nop the shift stack instruction
                    instruction.OpCode = InstructionSetIndependentOpCode.Nop;
                    instruction.Operands = [];
                }

                // Correct offset for stack operands
                for (var i = 0; i < instruction.Operands.Length; i++)
                {
                    var op = instruction.Operands[i];

                    if (op.Data is IsilStackOperand offset)
                    {
                        // TODO: sometimes try catch causes something weird, probably indirect jump somewhere, so some instructions are in cfg but not in _instructionState
                        var state = _instructionState[instruction].Size;
                        var actual = state + offset.Offset;
                        instruction.Operands[i] = InstructionSetIndependentOperand.MakeStack(actual);
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
        for (var i = 0; i < block.isilInstructions.Count; i++)
        {
            var instruction = block.isilInstructions[i];

            _instructionState[instruction] = currentState;

            if (instruction.OpCode.Mnemonic == IsilMnemonic.ShiftStack)
            {
                var offset = (int)(((IsilImmediateOperand)instruction.Operands[0].Data).Value);
                currentState = currentState.Copy();
                currentState.Size += offset;
            }
            else if (i == block.isilInstructions.Count - 1 && block.IsTailCall)
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
            if (operand.Data is IsilStackOperand offset)
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
            for (var i = 0; i < instruction.Operands.Length; i++)
            {
                var operand = instruction.Operands[i];

                if (operand.Data is IsilStackOperand offset)
                    instruction.Operands[i] =
                        InstructionSetIndependentOperand.MakeRegister(offsetToRegister[offset.Offset]);
            }
        }
    }
}
