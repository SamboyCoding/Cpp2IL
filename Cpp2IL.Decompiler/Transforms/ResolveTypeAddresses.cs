using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Resolves types that are referenced as constant addresses.
/// </summary>
public class ResolveTypeAddresses : ITransform
{
    public void Apply(Method method, IContext context)
    {
        foreach (var instruction in method.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                // Not source
                if (!instruction.SourcesAndConstants.Contains(operand))
                    continue;

                if (operand is not MemoryAddress { IsConstant: true } memory) continue;

                var type = context.GetTypeByAddress((ulong)memory.Addend);

                if (type != null)
                    instruction.Operands[i] = type;
            }
        }
    }
}
