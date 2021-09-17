using Cpp2IL.Core.Analysis.ResultModels;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public abstract class PostProcessor<T>
    {
        public abstract void PostProcess(MethodAnalysis<T> analysis);
    }
}