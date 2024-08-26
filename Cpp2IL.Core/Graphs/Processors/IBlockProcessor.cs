using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Graphs.Processors;

internal interface IBlockProcessor
{
    public void Process(MethodAnalysisContext methodAnalysisContext, Block<InstructionSetIndependentInstruction> block);
}
