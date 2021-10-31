using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class EbpOffsetToLocalAction : BaseAction<Instruction>
    {
        private LocalDefinition? localBeingRead;
        private string _destReg;
        private LocalDefinition? _localMade;

        public EbpOffsetToLocalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            localBeingRead = StackPointerUtils.GetLocalReferencedByEBPRead(context, instruction);

            if (localBeingRead == null) return;
            
            RegisterUsedLocal(localBeingRead, context);
            
            _destReg = Utils.Utils.GetRegisterNameNew(instruction.Op0Register);

            _localMade = context.MakeLocal(localBeingRead.Type!, reg: _destReg).WithParameter(localBeingRead.ParameterDefinition);
            // context.SetRegContent(_destReg, localBeingRead);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (localBeingRead == null || _localMade == null)
                throw new TaintedInstructionException("Couldn't resolve parameter or local being read");

            if (_localMade.Variable == null)
                return Array.Empty<Mono.Cecil.Cil.Instruction>();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            ret.AddRange(localBeingRead.GetILToLoad(context, processor));
            
            ret.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type} {_localMade?.Name} = {localBeingRead?.Name} //Stored Parameter/Local read";
        }

        public override string ToTextSummary()
        {
            return $"Copies EBP-Param {localBeingRead} to register {_destReg} as new local {_localMade}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}