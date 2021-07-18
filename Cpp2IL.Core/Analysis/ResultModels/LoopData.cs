using Cpp2IL.Core.Analysis.Actions.Important;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class LoopData : AnalysisState
    {
        public ulong ipFirstInstruction;
        public ulong ipFirstInstructionNotInLoop;
        public ComparisonAction loopCondition;
    }
}