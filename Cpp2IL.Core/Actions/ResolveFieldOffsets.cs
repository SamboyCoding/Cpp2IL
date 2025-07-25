using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public class ResolveFieldOffsets : IAction
{
    public void Apply(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is not MemoryOperand memory)
                    continue;

                // Has to be [base (local) + addend (field offset)]
                if (memory.Index != null || memory.Scale != 0)
                    continue;

                if (memory.Base is not LocalVariable local || local?.Type == null)
                    continue;

                var field = local.Type.Fields.FirstOrDefault(f => f.BackingData?.FieldOffset == memory.Addend);

                if (field == null) // TODO: Support nested fields (Field1.Field2.Field3)
                    continue;

                instruction.Operands[i] = new FieldReference(field, local, (int)memory.Addend);
            }
        }
    }
}
