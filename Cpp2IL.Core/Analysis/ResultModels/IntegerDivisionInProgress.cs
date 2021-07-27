namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IntegerDivisionInProgress
    {
        public IAnalysedOperand OriginalValue;
        public ulong MultipliedBy;
        public int ShiftCount;
        public bool TookTopHalf;
        public bool IsComplete;

        public IntegerDivisionInProgress(IAnalysedOperand originalValue, ulong multipliedBy)
        {
            OriginalValue = originalValue;
            MultipliedBy = multipliedBy;
        }
    }
}