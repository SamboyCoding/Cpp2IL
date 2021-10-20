using System.Text;
using Cpp2IL.Core.Analysis.Actions.Base;

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
        public void RunPostProcessors();
    }
}