﻿using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class JumpIfGreaterThanAction : ConditionalJumpAction
    {
        public JumpIfGreaterThanAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //All handled by base class
        }

        protected override string GetPseudocodeCondition()
        {
            //Invert condition, so <=, not >
            return $"({GetArgumentOnePseudocodeValue()} <= {GetArgumentTwoPseudocodeValue()})";
        }

        protected override OpCode GetJumpOpcode()
        {
            return OpCodes.Bgt;
        }

        protected override string GetTextSummaryCondition()
        {
            if (associatedCompare == null)
                return "the compare showed that it was greater than";

            return $"{associatedCompare.ArgumentOne} is greater than {associatedCompare.ArgumentTwo}";
        }
    }
}