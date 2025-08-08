using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public class ResolveGetters : IAction // Because of il2cpp fields [local @ reg+offset] sometimes can't be resolved
{
    public void Apply(MethodAnalysisContext method)
    {
        var isGetter = method.Name.StartsWith("get_");
        var instructions = method.ControlFlowGraph!.Instructions;

        if (!isGetter)
            return;

        // Default get: Return [this @ reg+offset]
        if (instructions.Count == 1)
        {
            var instr = instructions[0];

            if (instr.OpCode != OpCode.Return
                || instr.Operands.Count < 1
                || instr.Operands[0] is not MemoryOperand memory
                || memory.Index != null || memory.Scale != 0
                || memory.Base is not LocalVariable local)
                return;

            var fieldName = $"<{method.Name[4..]}>k__BackingField";

            var field = method.DeclaringType!.Fields.Find(f => f.Name == fieldName); // TODO: Check the offset while ignoring all il2cpp fields
            if (field == null)
                return;

            instr.Operands[0] = new FieldReference(field, local, (int)memory.Addend);
        }
    }
}
