using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class MultiplyRegByFieldAction : BaseAction<Instruction>
    {
        private LocalDefinition? _op1;
        private string? _regName;
        private LocalDefinition? _localMade;
        private LocalDefinition? ReadFrom;
        private FieldUtils.FieldBeingAccessedData? FieldRead;
        private string? _destRegName;

        public MultiplyRegByFieldAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            
            _regName = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            _op1 = context.GetLocalInReg(_regName);
            
            if(_op1 is {})
                RegisterUsedLocal(_op1, context);
            
            var sourceRegName = X86Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destRegName = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            var sourceFieldOffset = instruction.MemoryDisplacement;

            var readFrom = context.GetOperandInRegister(sourceRegName);

            TypeReference readFromType;
            if (readFrom is ConstantDefinition {Value: NewSafeCastResult<Instruction> result})
            {
                readFromType = result.castTo;
                ReadFrom = result.original;
                RegisterUsedLocal(ReadFrom, context);
            }
            else if(readFrom is LocalDefinition {IsMethodInfoParam: false} l && l.Type?.Resolve() != null)
            {
                ReadFrom = l;
                readFromType = ReadFrom!.Type!;
                RegisterUsedLocal(ReadFrom, context);
            } else
            {
                AddComment($"This shouldn't be a field read? Op in reg {sourceRegName} is {context.GetOperandInRegister(sourceRegName)}, offset is {sourceFieldOffset} (0x{sourceFieldOffset:X})");
                return;
            }

            FieldRead = FieldUtils.GetFieldBeingAccessed(readFromType, sourceFieldOffset, false);
            
            if(FieldRead == null) return;

            _localMade = context.MakeLocal(FieldRead.GetFinalType(), reg: _destRegName);;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (FieldRead is null)
                throw new TaintedInstructionException("FieldRead was null");
            
            if (_op1 is null || _localMade?.Variable is null)
                throw new TaintedInstructionException("Operand we were adding to is null or local made was stripped");

            List<Mono.Cecil.Cil.Instruction> instructions = new();
            
            instructions.AddRange(_op1.GetILToLoad(context, processor));
            
            instructions.AddRange(FieldRead.GetILToLoad(processor));
            
            instructions.Add(processor.Create(OpCodes.Mul));
            
            instructions.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));

            return instructions.ToArray();
        }
        
        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type} {_localMade?.Name} = {_op1?.GetPseudocodeRepresentation()} * {ReadFrom?.Name}.{FieldRead}";
        }

        public override string ToTextSummary()
        {
            return $"Multiplies {_op1} by the field {FieldRead} from {ReadFrom}, and stores the result in new local {_localMade} in register {_regName}";
        }

        public override bool IsImportant()
        {
            return true;
        }
        
    }
}