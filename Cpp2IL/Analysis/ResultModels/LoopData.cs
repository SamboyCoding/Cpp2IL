using Cpp2IL.Analysis.Actions;
using Cpp2IL.Analysis.Actions.Important;
using Iced.Intel;

namespace Cpp2IL.Analysis.ResultModels
{
    public class LoopData : AnalysisState
    {
        public ulong ipFirstInstruction;
        public ulong ipFirstInstructionNotInLoop;
        public ComparisonAction loopCondition;
    }
}