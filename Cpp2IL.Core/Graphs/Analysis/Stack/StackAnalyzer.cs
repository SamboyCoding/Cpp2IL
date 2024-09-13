using System;
using System.Collections.Generic;
using LibCpp2IL;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using System.Linq;

namespace Cpp2IL.Core.Graphs.Analysis.Stack;

public sealed class StackAnalyzer
{
    private HashSet<Block<InstructionSetIndependentInstruction>> visited = [];

    // TODO: Should stack state be per instruction or per block?
    private Dictionary<Block<InstructionSetIndependentInstruction>, StackEntry> inComingDelta = [];
    private Dictionary<Block<InstructionSetIndependentInstruction>, StackEntry> outGoingDelta = [];

    // debug
    public static int unbalancedStackCount { get; private set; } = 0;
    public static int balanacedStackCount { get; private set; } = 0;

    private StackAnalyzer() { }

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
            analyzer.inComingDelta[graph.EntryBlock] = new StackEntry();
            var archSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
            analyzer.TraverseGraph(graph.EntryBlock, archSize);
            var outDelta = analyzer.outGoingDelta[graph.ExitBlock];
            if (outDelta.StackState.Count != 0)
            {
                unbalancedStackCount++;
            }
            else
            {
                balanacedStackCount++;
                foreach(var block in graph.Blocks)
                {
                    // TODO: Replace push instructions with a move Stack 0x20, reg1 or whatever
                    // push instructions can be nopped? Same with shiftstack instructions?
                    if (block.BlockType == BlockType.Call)
                    {
                        var callInstruction = block.Instructions[^1];
                        
                        var stackState = analyzer.outGoingDelta[block].StackState;


                        var stackParams = callInstruction.Operands.Where(op => op.Type == InstructionSetIndependentOperand.OperandType.StackOffset);
                        // TODO: translate stack offsets relative to call instruction to stack offsets relative to the base of the stack for the function
                    }
                }

            }
        }
        catch (Exception e)
        {
            unbalancedStackCount++;
        }
    }

    private void TraverseGraph(Block<InstructionSetIndependentInstruction> block, int archSize)
    {
        var blockDelta = inComingDelta[block].Clone();

        // TODO: Handle interrupt blocks, should we just remove them?
        if (block.BlockType == BlockType.Call && block.Successors.Count == 1 && block.Successors[0].BlockType == BlockType.Exit)
        {
            // Tail call / CallNoReturn = Flush stack?
            blockDelta.StackState.Clear();
            outGoingDelta[block] = blockDelta;
        }
        else
        {
            foreach (var instruction in block.Instructions)
            {
                switch (instruction.OpCode.Mnemonic)
                {
                    case IsilMnemonic.Push:
                        blockDelta.PushEntry("push");
                        break;
                    case IsilMnemonic.Pop:
                        blockDelta.PopEntry();
                        break;
                    case IsilMnemonic.ShiftStack:
                        var value = (int)((IsilImmediateOperand)instruction.Operands[0].Data).Value;
                        if (value % archSize != 0)
                        {
                            throw new Exception("Unaligned stack shift");
                        } else
                        {
                            for (int i = 0; i < Math.Abs(value / archSize); i++)
                            {
                                if (value < 0)
                                {
                                    blockDelta.PushEntry("allocated space");
                                }
                                else
                                {
                                    blockDelta.PopEntry();
                                }
                            }
                        }
                        
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
            }
            else
            {
                var expectedDelta = inComingDelta[succ];

                if (expectedDelta != blockDelta)
                {
                    // TODO: Investigate Guid\.ctor_Byte[].dot, stack appears to be well formed but results in unbalanced stack somehow
                    throw new Exception("Unbalanced stack");
                }
                inComingDelta[succ] = blockDelta;
            }
        }
    }
}
