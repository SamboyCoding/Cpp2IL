using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public interface IComparisonArgument
    {
        public string GetPseudocodeRepresentation();

        public Instruction[] GetILToLoad<TAnalysis>(MethodAnalysis<TAnalysis> context, ILProcessor processor);
    }
}