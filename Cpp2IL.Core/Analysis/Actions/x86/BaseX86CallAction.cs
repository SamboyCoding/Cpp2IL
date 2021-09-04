using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public abstract class BaseX86CallAction : AbstractCallAction<Instruction>
    {
        protected BaseX86CallAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }
    }
}