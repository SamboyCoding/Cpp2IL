using System.Linq;
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
            // TODO: Check if it shows up in any other
            if (instruction.OpCode != OpCode.Move && instruction.OpCode != OpCode.LoadAddress)
            {
                continue;
            }

            if ((instruction.Operands[0] is not Register) || (instruction.Operands[1] is not MemoryOperand))
            {
                continue;
            }

            var memoryOp = (MemoryOperand)instruction.Operands[1];
            if (memoryOp.Base == null && memoryOp.Index == null && memoryOp.Scale == 0)
            {
                var val = LibCpp2IlMain.GetLiteralByAddress((ulong)memoryOp.Addend);
                if (val == null)
                {
                    // Try instead check if its type metadata usage
                    var metadataUsage = LibCpp2IlMain.GetTypeGlobalByAddress((ulong)memoryOp.Addend);
                    if (metadataUsage != null && method.DeclaringType is not null)
                    {
                        var typeAnalysisContext = metadataUsage.ToContext(method.DeclaringType!.DeclaringAssembly);
                        if (typeAnalysisContext != null)
                            instruction.Operands[1] = typeAnalysisContext;
                    }

                    continue;
                }

                instruction.Operands[1] = val;
            }
        }
    }
}
