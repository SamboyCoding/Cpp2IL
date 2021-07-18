using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public abstract class PostProcessor
    {
        public abstract void PostProcess(MethodAnalysis analysis, MethodDefinition definition);
    }
}