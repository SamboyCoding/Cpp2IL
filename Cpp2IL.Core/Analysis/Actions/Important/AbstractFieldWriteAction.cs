using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public abstract class AbstractFieldWriteAction : BaseAction
    {
        public LocalDefinition? InstanceBeingSetOn;
        public FieldUtils.FieldBeingAccessedData? FieldWritten;
        protected AbstractFieldWriteAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }
        
        
    }
}