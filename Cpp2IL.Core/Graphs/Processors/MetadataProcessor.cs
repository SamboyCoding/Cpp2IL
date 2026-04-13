using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Graphs.Processors;

internal class MetadataProcessor : IBlockProcessor
{
    public void Process(MethodAnalysisContext methodAnalysisContext, Block block)
    {
        foreach (var instruction in block.isilInstructions)
        {
            // TODO: Check if it shows up in any other
            if (instruction.OpCode != InstructionSetIndependentOpCode.Move)
            {
                continue;
            }

            if (instruction.Operands[0].Type != InstructionSetIndependentOperand.OperandType.Register || instruction.Operands[1].Type != InstructionSetIndependentOperand.OperandType.Memory)
            {
                continue;
            }

            var memoryOp = (IsilMemoryOperand)instruction.Operands[1].Data;
            if (memoryOp.Base == null && memoryOp.Index == null && memoryOp.Scale == 0)
            {
                var val = methodAnalysisContext.AppContext.LibCpp2IlContext.GetLiteralByAddress((ulong)memoryOp.Addend);
                if (val == null)
                {
                    // Try instead check if its type metadata usage
                    var metadataUsage = methodAnalysisContext.AppContext.LibCpp2IlContext.GetTypeGlobalByAddress((ulong)memoryOp.Addend);
                    if (metadataUsage != null && methodAnalysisContext.DeclaringType is not null)
                    {
                        var typeAnalysisContext = metadataUsage.ToContext(methodAnalysisContext.DeclaringType!.DeclaringAssembly);
                        if (typeAnalysisContext != null)
                            instruction.Operands[1] = InstructionSetIndependentOperand.MakeTypeMetadataUsage(typeAnalysisContext);
                    }

                    continue;
                }

                instruction.Operands[1] = InstructionSetIndependentOperand.MakeImmediate(val);
            }
        }
    }
}
