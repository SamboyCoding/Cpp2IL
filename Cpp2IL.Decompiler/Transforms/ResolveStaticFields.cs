using AsmResolver.DotNet;
using Cpp2IL.Decompiler.ControlFlow;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Resolves all static field operands.
/// </summary>
public class ResolveStaticFields : ITransform
{
    public void Apply(Method method, IContext context)
    {
        foreach (var instruction in method.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is LocalVariable { Field: not null } local)
                {
                    var type = GetLocalSourceType(method.ControlFlowGraph, local);

                    if (type != null && local.Field.IsStatic)
                        instruction.Operands[i] = local.Field;
                }
            }
        }
    }

    private static TypeDefinition? GetLocalSourceType(ControlFlowGraph graph, LocalVariable local)
    {
        foreach (var instruction in graph.AllInstructions)
        {
            if (instruction.OpCode is OpCode.Move or OpCode.LoadAddress
                && instruction.Operands[0] is LocalVariable destLocal && destLocal.Register == local.Register
                && instruction.Operands[1] is TypeDefinition type)
                return type;
        }

        return null;
    }
}
