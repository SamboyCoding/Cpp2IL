using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;

namespace Cpp2IL.Analysis.PostProcessActions
{
    public abstract class PostProcessor
    {
        public abstract void PostProcess(MethodAnalysis analysis, MethodDefinition definition);
    }
}