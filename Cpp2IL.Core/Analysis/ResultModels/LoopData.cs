using Cpp2IL.Core.Analysis.Actions.Base;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class LoopData<T> : AnalysisState
    {
        public ulong ipFirstInstruction;
        public ulong ipFirstInstructionNotInLoop;
        public AbstractComparisonAction<T> loopCondition;
    }
}