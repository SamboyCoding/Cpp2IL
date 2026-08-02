using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

// Remove null checks which are explict in il2cpp but implicit in IL
public static class NullCheckRemover
{
    public static void Run(MethodAnalysisContext method) => Run(method.ControlFlowGraph!);

    public static void Run(ISILControlFlowGraph cfg)
    {
        var defOf = BuildDefMap(cfg);
        var removedAny = false;

        foreach (var block in cfg.Blocks)
        {
            if (block.BlockType != BlockType.TwoWay || block.Instructions.Count == 0)
                continue;

            var terminator = block.Instructions[^1];

            if (terminator.OpCode != OpCode.ConditionalJump)
                continue;

            if (terminator.Operands[0] is not Block target || !IsNullCheckThrowBlock(target))
                continue;

            if (terminator.Operands[1] is not LocalVariable condition
                || !defOf.TryGetValue(condition, out var definition)
                || definition.OpCode != OpCode.CheckEqual
                || definition.Operands[2] is not Immediate { Value: 0 })
                continue;

            terminator.OpCode = OpCode.Nop;
            terminator.SetOperands();

            block.Successors.Remove(target);
            target.Predecessors.Remove(block);
            block.CalculateBlockType();
            removedAny = true;
        }

        if (!removedAny)
            return;

        foreach (var block in cfg.Blocks.ToList())
        {
            if (block == cfg.EntryBlock || block.Predecessors.Count > 0 || !IsNullCheckThrowBlock(block))
                continue;

            foreach (var successor in block.Successors)
                successor.Predecessors.Remove(block);

            block.Successors.Clear();
            cfg.Blocks.Remove(block);
        }

        DeadCodeEliminator.Run(cfg);
    }

    private static bool IsNullCheckThrowBlock(Block block)
    {
        var sawThrow = false;

        foreach (var instruction in block.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Nop:
                case OpCode.Return when sawThrow:
                    continue;

                case OpCode.Throw when instruction.Operands is [TypeAnalysisContext { FullName: "System.NullReferenceException" }]:
                    sawThrow = true;
                    continue;

                default:
                    return false;
            }
        }

        return sawThrow;
    }

    private static Dictionary<LocalVariable, Instruction> BuildDefMap(ISILControlFlowGraph cfg)
    {
        var defs = new Dictionary<LocalVariable, Instruction>();

        foreach (var instruction in cfg.Instructions)
            if (instruction.Destination is LocalVariable local)
                defs[local] = instruction;

        return defs;
    }
}
