using System.Text;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis
{
    public interface IAsmAnalyzer
    {
        public void AnalyzeMethod();
        public StringBuilder GetFullDumpNoIL();
        public void BuildMethodFunctionality();
        public StringBuilder BuildILToString();
        public StringBuilder GetPseudocode();
        public StringBuilder GetWordyFunctionality();
        public void RunActionPostProcessors();
        public void RunILPostProcessors(MethodBody body);
    }
}