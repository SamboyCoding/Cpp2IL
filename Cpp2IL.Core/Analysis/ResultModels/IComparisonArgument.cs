using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public interface IComparisonArgument<T>
    {
        public string GetPseudocodeRepresentation();

        public Instruction[] GetILToLoad(MethodAnalysis<T> context, ILProcessor processor);
    }
}