using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class PlusOneOrMinusOneAction : BaseAction<Instruction>
    {
        private LocalDefinition? _localBeingAddedTo;
        private LocalDefinition? _localMade;
        private bool Adding;

        public PlusOneOrMinusOneAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            string destReg = X86Utils.GetRegisterNameNew(instruction.Op0Register);

            string regBeingAddedTo = X86Utils.GetRegisterNameNew(instruction.MemoryBase);

            _localBeingAddedTo = context.GetLocalInReg(regBeingAddedTo);

            if (_localBeingAddedTo?.Type == null) return;

            _localMade = context.MakeLocal(_localBeingAddedTo.Type, reg: destReg);

            RegisterUsedLocal(_localBeingAddedTo, context);


            if ((long)instruction.MemoryDisplacement64 == 1)
                Adding = true;
        }

        public override bool IsImportant() => true;

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_localBeingAddedTo == null)
                throw new TaintedInstructionException("Local being added to was null");

            if (_localMade?.Variable == null)
                throw new TaintedInstructionException("Local made was stripped");    
            
            List<Mono.Cecil.Cil.Instruction> instructions = new();
            
            if (Adding)
            {
                instructions.AddRange(_localBeingAddedTo.GetILToLoad(context, processor));
                instructions.Add(Mono.Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4_1));
                instructions.Add(Mono.Cecil.Cil.Instruction.Create(OpCodes.Add));
                instructions.Add(Mono.Cecil.Cil.Instruction.Create(OpCodes.Stloc, _localMade.Variable));
            }
            else
            {
                instructions.AddRange(_localBeingAddedTo.GetILToLoad(context, processor));
                instructions.Add(Mono.Cecil.Cil.Instruction.Create(OpCodes.Ldc_I4_1));
                instructions.Add(Mono.Cecil.Cil.Instruction.Create(OpCodes.Sub));
                instructions.Add(Mono.Cecil.Cil.Instruction.Create(OpCodes.Stloc, _localMade.Variable));
            }

            return instructions.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type?.FullName} {_localMade?.Name} = {_localBeingAddedTo?.Name} {(Adding ? "+" : "-")} 1";
        }

        public override string ToTextSummary()
        {
           return $"{(Adding ? "Adds" : "Subtracts")} 1 {(Adding ? "to" : "from")} {_localBeingAddedTo?.Name} and stores the result in {_localMade?.Name}";
        }
    }
}