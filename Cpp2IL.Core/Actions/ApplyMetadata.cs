using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.Actions;

public class ApplyMetadata : IAction
{
    public void Apply(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode != OpCode.Move)
                continue;

            if ((instruction.Operands[0] is not LocalVariable) || (instruction.Operands[1] is not MemoryOperand memory))
                continue;

            if (memory.Base == null && memory.Index == null && memory.Scale == 0)
            {
                var stringLiteral = LibCpp2IlMain.GetLiteralByAddress((ulong)memory.Addend);

                if (stringLiteral == null)
                {
                    // Try instead check if its type metadata usage
                    var metadataUsage = LibCpp2IlMain.GetTypeGlobalByAddress((ulong)memory.Addend);
                    if (metadataUsage != null && method.DeclaringType is not null)
                    {
                        var typeAnalysisContext = metadataUsage.ToContext(method.DeclaringType!.DeclaringAssembly);
                        if (typeAnalysisContext != null)
                            instruction.Operands[1] = typeAnalysisContext;
                    }

                    continue;
                }

                instruction.Operands[1] = stringLiteral;
            }
        }
    }
}
