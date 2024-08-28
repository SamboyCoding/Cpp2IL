using System;
using System.Collections.Generic;
using LibCpp2IL;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using System.Diagnostics;
namespace Cpp2IL.Core.Graphs.Analysis;

public sealed class StackAnalyzer
{
    public HashSet<Block<InstructionSetIndependentInstruction>> visited = [];
    public Dictionary<Block<InstructionSetIndependentInstruction>, int> inComingDelta = [];
    public Dictionary<Block<InstructionSetIndependentInstruction>, int> outGoingDelta = [];

    public static int unbalancedStackCount { get; private set; } = 0;
    public static int balanacedStackCount { get; private set; } = 0;

    private StackAnalyzer() {}

    public static void Analyze(MethodAnalysisContext context)
    {
        try
        {
            var graph = context.ControlFlowGraph;
            if (graph == null)
            {
                return;
            }
            var analyzer = new StackAnalyzer();
            analyzer.inComingDelta[graph.EntryBlock] = 0;
            int archSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
            analyzer.TraverseGraph(graph.EntryBlock, archSize);
            var outDelta = analyzer.outGoingDelta[graph.ExitBlock];
            if (outDelta != 0)
            {
                unbalancedStackCount++;
            }
            else
            {
                balanacedStackCount++;
            }
        } catch (Exception e)
        {
            unbalancedStackCount++;
        }
    }

    private void TraverseGraph(Block<InstructionSetIndependentInstruction> block, int archSize)
    {
        var blockDelta = inComingDelta[block];

        if (block.BlockType == BlockType.Call && block.Successors.Count == 1 && block.Successors[0].BlockType == BlockType.Exit)
        {
            // Tail call / CallNoReturn
            blockDelta = 0;
            outGoingDelta[block] = blockDelta;
        } else
        {
            foreach (var instruction in block.Instructions)
            {
                switch (instruction.OpCode.Mnemonic)
                {
                    case IsilMnemonic.Push:
                        blockDelta -= archSize;
                        break;
                    case IsilMnemonic.Pop:
                        blockDelta += archSize;
                        break;
                    case IsilMnemonic.ShiftStack:
                        blockDelta += (int)((IsilImmediateOperand)instruction.Operands[0].Data).Value;
                        break;
                }
            }
            outGoingDelta[block] = blockDelta;
        }

        foreach (var succ in block.Successors)
        {
            if (!visited.Contains(succ))
            {
                inComingDelta[succ] = blockDelta;
                visited.Add(succ);
                TraverseGraph(succ, archSize);
            } else
            {
                var expectedDelta = inComingDelta[succ];

                if (expectedDelta != blockDelta)
                {
                    throw new Exception("Unbalanced stack");
                }
                inComingDelta[succ] = blockDelta;
            }
        }
    }
}
