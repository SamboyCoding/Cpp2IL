using Cpp2IL.Core.Analysis.Actions.Important;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class LoopData<T> : AnalysisState<T>
    {
        public ulong ipFirstInstruction;
        public ulong ipFirstInstructionNotInLoop;
        public ComparisonAction loopCondition;
    }
}