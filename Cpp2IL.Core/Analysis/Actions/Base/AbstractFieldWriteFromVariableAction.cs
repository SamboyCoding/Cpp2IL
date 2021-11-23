using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractFieldWriteFromVariableAction<T> : AbstractFieldWriteAction<T>
    {
        public IAnalysedOperand? SourceOperand;

        protected AbstractFieldWriteFromVariableAction(MethodAnalysis<T> context, T instruction) : base(context, instruction)
        {
        }

        protected override Instruction[] GetIlToLoadValue(MethodAnalysis<T> context, ILProcessor processor) => SourceOperand?.GetILToLoad(context, processor) ?? throw new TaintedInstructionException("Value read is null");
    }
}