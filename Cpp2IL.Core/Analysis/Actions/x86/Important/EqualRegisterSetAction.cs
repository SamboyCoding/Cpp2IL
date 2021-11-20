﻿using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class EqualRegisterSetAction : ConditionalRegisterSetAction
    {
        public EqualRegisterSetAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
        }

        protected override string GetTextSummaryCondition()
        {
            return $"{_associatedCompare?.ArgumentOne?.GetPseudocodeRepresentation()} is equal to {_associatedCompare?.ArgumentTwo?.GetPseudocodeRepresentation()}";
        }

        protected override string GetPseudocodeCondition()
        {
            return $"{_associatedCompare?.ArgumentOne?.GetPseudocodeRepresentation()} == {_associatedCompare?.ArgumentTwo?.GetPseudocodeRepresentation()}";
        }

        protected override Mono.Cecil.Cil.Instruction[] GetComparisonIl(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            return new[] { processor.Create(OpCodes.Ceq) };
        }
    }
}