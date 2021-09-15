using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public abstract class PostProcessor<T>
    {
        public abstract void PostProcess(MethodAnalysis<T> analysis, MethodDefinition definition);
    }
}