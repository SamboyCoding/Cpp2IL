using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class NotEqualRegisterSetAction : ConditionalRegisterSetAction
    {
        public NotEqualRegisterSetAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
        }

        protected override string GetTextSummaryCondition()
        {
            return $"{_associatedCompare?.ArgumentOne?.GetPseudocodeRepresentation()} isn't equal to {_associatedCompare?.ArgumentTwo?.GetPseudocodeRepresentation()}";
        }

        protected override string GetPseudocodeCondition()
        {
            return $"{_associatedCompare?.ArgumentOne?.GetPseudocodeRepresentation()} != {_associatedCompare?.ArgumentTwo?.GetPseudocodeRepresentation()}";
        }

        protected override Mono.Cecil.Cil.Instruction[] GetComparisonIl(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            return new []
            {
                processor.Create(OpCodes.Ceq),
                processor.Create(OpCodes.Ldc_I4_0),
                processor.Create(OpCodes.Ceq)
            };
        }
    }
}