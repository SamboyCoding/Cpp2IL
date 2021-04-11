using Cpp2IL.Analysis.Actions.Important;

namespace Cpp2IL.Analysis.ResultModels
{
    public class LoopData : AnalysisState
    {
        public ulong ipFirstInstruction;
        public ulong ipFirstInstructionNotInLoop;
        public ComparisonAction loopCondition;
    }
}