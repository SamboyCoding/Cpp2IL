using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public abstract class ILPostProcessor<T>
    {
        public abstract void PostProcess(MethodAnalysis<T> analysis, MethodBody body);
    }
}