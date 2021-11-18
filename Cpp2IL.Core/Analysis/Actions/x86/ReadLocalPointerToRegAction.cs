﻿using System.Diagnostics;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Instruction = Iced.Intel.Instruction;
using Mono.Cecil.Cil;


namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class ReadLocalPointerToRegAction : BaseAction<Instruction>
    {
        private LocalPointer? LocalPointer;
        private LocalDefinition? _localMade;
        public ReadLocalPointerToRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            LocalPointer = context.GetConstantInReg(X86Utils.GetRegisterNameNew(instruction.MemoryBase))?.Value as LocalPointer;

            if (LocalPointer == null)
                return;
            
            
            RegisterUsedLocal(LocalPointer.Local, context);
            
            string destReg = X86Utils.GetRegisterNameNew(instruction.Op0Register);

            if(LocalPointer.Local.Type != null)
                _localMade = context.MakeLocal(LocalPointer.Local.Type, reg: destReg);
            
           
        }

        public override bool IsImportant()
        {
            return true;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (LocalPointer?.Local?.Variable == null)
                throw new TaintedInstructionException("Local being dereferenced was stripped");
            
            if (_localMade?.Variable == null)
                throw new TaintedInstructionException("Local was stripped");
            
            
            return new[]
            {
                context.GetIlToLoad(LocalPointer.Local, processor),
                processor.Create(OpCodes.Stloc, _localMade.Variable)
            };
        }
        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Name} = {LocalPointer?.Local?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"Dereference and moves local {LocalPointer?.Local?.Name} to {_localMade?.Name}";
        }
    }
}