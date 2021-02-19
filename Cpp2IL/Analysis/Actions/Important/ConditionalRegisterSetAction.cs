using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public abstract class ConditionalRegisterSetAction : BaseAction
    {
        protected readonly ComparisonAction? _associatedCompare;
        protected readonly string? _regToSet;
        private readonly LocalDefinition _localMade;

        public ConditionalRegisterSetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _regToSet = Utils.GetRegisterNameNew(instruction.Op0Register);
            _associatedCompare = (ComparisonAction?) context.Actions.LastOrDefault(a => a is ComparisonAction);

            _localMade = context.MakeLocal(Utils.BooleanReference, reg: _regToSet);
        }

        protected abstract string GetTextSummaryCondition();
        
        protected abstract string GetPseudocodeCondition();

        protected abstract Mono.Cecil.Cil.Instruction GetComparisonIl(MethodAnalysis context, ILProcessor processor);

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            if (_associatedCompare?.ArgumentOne == null || _associatedCompare?.ArgumentTwo == null)
                throw new TaintedInstructionException();

            ret.AddRange(_associatedCompare.ArgumentOne.GetILToLoad(context, processor));
            ret.AddRange(_associatedCompare.ArgumentTwo.GetILToLoad(context, processor));
            
            ret.Add(GetComparisonIl(context, processor));
            
            ret.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));
            
            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade.Type!.FullName} {_localMade.Name} = {GetPseudocodeCondition()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets the boolean {_localMade} in {_regToSet} to true if {GetTextSummaryCondition()}, otherwise false.";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}