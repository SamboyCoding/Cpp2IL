using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

// Remove null and bounds checks which are explicit in il2cpp but implicit in IL
public static class InjectedCheckRemover
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

            if (terminator.Operands[0] is not Block target || GetInjectedThrowType(target) is not { } thrownType)
                continue;

            if (terminator.Operands[1] is not LocalVariable condition
                || !defOf.TryGetValue(condition, out var definition)
                || !IsInjectedCheck(definition, thrownType))
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

        // delete any throw blocks
        cfg.RemoveUnreachableBlocks();
        DeadCodeEliminator.Run(cfg);
    }

    private static bool IsInjectedCheck(Instruction definition, string thrownType) =>
        thrownType switch
        {
            "System.NullReferenceException" => (definition.OpCode is OpCode.CheckEqual or OpCode.CheckNotEqual) &&
                                               ((definition.Operands.Count > 1 && IsZeroConstant(definition.Operands[1])) ||
                                                (definition.Operands.Count > 2 && IsZeroConstant(definition.Operands[2]))),
            "System.IndexOutOfRangeException" => definition.OpCode is >= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual,
            _ => false
        };

    private static bool IsZeroConstant(IOperand operand) => operand is Immediate { Value: 0 };

    // The full name of the exception if this block does nothing but throw an injected check's exception, else null.
    private static string? GetInjectedThrowType(Block block, int depth = 0)
    {
        if (depth > 2)
            return null;

        string? thrown = null;

        foreach (var instruction in block.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Nop or OpCode.Interrupt:
                case OpCode.Return when thrown != null:
                    continue;

                case OpCode.Jump when thrown != null:
                    continue;

                case OpCode.Jump when instruction.Operands.Count > 0 && instruction.Operands[0] is Block targetBlock:
                    return GetInjectedThrowType(targetBlock, depth + 1);

                case OpCode.Newobj when instruction.Operands.Count > 1
                    && instruction.Operands[1] is TypeAnalysisContext { FullName: "System.NullReferenceException" or "System.IndexOutOfRangeException" } newobjEx:
                    thrown = newobjEx.FullName;
                    continue;

                case OpCode.Throw when thrown == null
                    && instruction.Operands is [TypeAnalysisContext { FullName: "System.NullReferenceException" or "System.IndexOutOfRangeException" } exception]:
                    thrown = exception.FullName;
                    continue;

                case OpCode.Throw when instruction.Operands is [LocalVariable local]
                    && (local.Type?.FullName is "System.NullReferenceException" or "System.IndexOutOfRangeException" || thrown != null):
                    thrown ??= local.Type?.FullName;
                    continue;

                default:
                    return null;
            }
        }

        return thrown;
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
