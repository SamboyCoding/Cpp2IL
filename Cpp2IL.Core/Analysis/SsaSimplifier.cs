using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Copy and constant propagation, performed while the graph is still in SSA form.
/// </summary>
public static class SsaSimplifier
{
    public static void Run(MethodAnalysisContext method) => Run(method.ControlFlowGraph!, method.ParameterLocals);

    public static void Run(ISILControlFlowGraph cfg, List<LocalVariable> parameterLocals)
    {
        // dest -> value for every forwardable copy/constant. SSA's single-assignment property means a
        // local is defined at most once, so there is never a conflicting entry for the same key.
        var forwarded = new Dictionary<LocalVariable, IOperand>();

        foreach (var block in cfg.Blocks)
            foreach (var instruction in block.Instructions)
                if (instruction.OpCode == OpCode.Move
                    && instruction.Operands[0] is LocalVariable dest
                    && !parameterLocals.Contains(dest)
                    && IsForwardable(instruction.Operands[1]))
                    forwarded[dest] = instruction.Operands[1];

        if (forwarded.Count == 0)
            return;

        // Collapse copy chains (t1 := a; t2 := t1; ...) so each local maps straight to its final value.
        var resolved = new Dictionary<LocalVariable, IOperand>();
        foreach (var dest in forwarded.Keys)
            resolved[dest] = Resolve(dest, forwarded);

        // One global substitution of every use.
        foreach (var block in cfg.Blocks)
            foreach (var instruction in block.Instructions)
                ReplaceUses(instruction, resolved);

        // A forwarded local is dead now - unless a use could not take its value (a constant cannot be
        // a memory base/index, so such a use keeps the original local). Drop the defining Move only
        // once the local truly has no reads left; the leftover nops are cleared by SsaForm.Remove.
        var reads = CollectReadLocals(cfg);
        foreach (var block in cfg.Blocks)
            foreach (var instruction in block.Instructions)
                if (instruction.OpCode == OpCode.Move
                    && instruction.Operands[0] is LocalVariable dest
                    && forwarded.ContainsKey(dest)
                    && !reads.Contains(dest))
                {
                    instruction.OpCode = OpCode.Nop;
                    instruction.SetOperands();
                }
    }

    // Follows local-to-local copies to the end of the chain. The visited set guards against a cycle a
    // malformed graph could present; a well-formed SSA graph (definitions dominate uses) has none.
    private static IOperand Resolve(LocalVariable dest, Dictionary<LocalVariable, IOperand> forwarded)
    {
        var value = forwarded[dest];
        var visited = new HashSet<LocalVariable> { dest };

        while (value is LocalVariable next && forwarded.TryGetValue(next, out var nextValue) && visited.Add(next))
            value = nextValue;

        return value;
    }

    private static void ReplaceUses(Instruction instruction, Dictionary<LocalVariable, IOperand> resolved)
    {
        // The single definition position (a Move/Call destination local) must not be rewritten - only
        // reads are forwarded. In SSA the local being eliminated never appears as a use of itself, so
        // skipping just its own definition operand is sufficient.
        var destination = instruction.Destination;

        for (var i = 0; i < instruction.Operands.Count; i++)
        {
            switch (instruction.Operands[i])
            {
                case LocalVariable local when !ReferenceEquals(local, destination) && resolved.TryGetValue(local, out var value):
                    instruction.SetOperand(i, value);
                    break;

                // A memory base/index must stay an address-holding local, so only a local replacement
                // is substituted there; a constant is left in place (which keeps the source Move alive).
                case MemoryOperand memory:
                    if (memory.Base is LocalVariable baseLocal && resolved.TryGetValue(baseLocal, out var baseValue) && baseValue is LocalVariable baseReplacement)
                        memory.Base = baseReplacement;
                    if (memory.Index is LocalVariable indexLocal && resolved.TryGetValue(indexLocal, out var indexValue) && indexValue is LocalVariable indexReplacement)
                        memory.Index = indexReplacement;
                    instruction.SetOperand(i, memory); // MemoryOperand is a struct, write the copy back
                    break;

                // Same as a memory base: the object a field is read from must stay a local.
                case FieldReference { Local: { } fieldLocal } field when resolved.TryGetValue(fieldLocal, out var fieldValue) && fieldValue is LocalVariable fieldReplacement:
                    field.Local = fieldReplacement;
                    break;
            }
        }
    }

    // Every local read by some instruction. The single write position (a plain local destination) is
    // excluded; memory and field operands always contribute their address/object locals as reads.
    private static HashSet<LocalVariable> CollectReadLocals(ISILControlFlowGraph cfg)
    {
        var reads = new HashSet<LocalVariable>();

        foreach (var block in cfg.Blocks)
            foreach (var instruction in block.Instructions)
            {
                var destination = instruction.Destination;

                foreach (var operand in instruction.Operands)
                {
                    switch (operand)
                    {
                        case LocalVariable local when !ReferenceEquals(local, destination):
                            reads.Add(local);
                            break;
                        case MemoryOperand memory:
                            if (memory.Base is LocalVariable baseLocal)
                                reads.Add(baseLocal);
                            if (memory.Index is LocalVariable indexLocal)
                                reads.Add(indexLocal);
                            break;
                        case FieldReference field when field.Local is { } fieldLocal:
                            reads.Add(fieldLocal);
                            break;
                    }
                }
            }

        return reads;
    }

    // Pure values that are safe to duplicate across uses: other locals (copies) and constants. Memory
    // and field loads are excluded so a load is never re-executed; they are handled post-SSA instead.
    private static bool IsForwardable(IOperand value) =>
        value switch
        {
            LocalVariable => true,
            MemoryOperand => false,
            FieldReference => false,
            _ => true
        };
}
