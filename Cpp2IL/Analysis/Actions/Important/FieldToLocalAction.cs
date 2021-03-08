using System;
using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class FieldToLocalAction : BaseAction
    {
        public FieldUtils.FieldBeingAccessedData? FieldRead;
        public LocalDefinition? LocalWritten;
        private string _destRegName;
        private LocalDefinition? _readFrom;

        public FieldToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var sourceRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destRegName = Utils.GetRegisterNameNew(instruction.Op0Register);
            var sourceFieldOffset = instruction.MemoryDisplacement;

            var readFrom = context.GetOperandInRegister(sourceRegName);

            TypeReference readFromType;
            if (readFrom is ConstantDefinition {Value: NewSafeCastResult result})
            {
                readFromType = result.castTo;
                _readFrom = result.original;
                RegisterUsedLocal(_readFrom);
            }
            else if(readFrom is LocalDefinition {IsMethodInfoParam: false} l && l.Type?.Resolve() != null)
            {
                _readFrom = l;
                readFromType = _readFrom!.Type!;
                RegisterUsedLocal(_readFrom);
            } else
            {
                AddComment($"This shouldn't be a field read? Op in reg {sourceRegName} is {context.GetOperandInRegister(sourceRegName)}, offset is {sourceFieldOffset} (0x{sourceFieldOffset:X})");
                return;
            }

            FieldRead = FieldUtils.GetFieldBeingAccessed(readFromType, sourceFieldOffset, false);
            
            if(FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.GetFinalType(), reg: _destRegName);
            RegisterDefinedLocalWithoutSideEffects(LocalWritten);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (LocalWritten == null || _readFrom == null || FieldRead == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            //Load object
            ret.AddRange(_readFrom.GetILToLoad(context, processor));

            //Access field
            ret.AddRange(FieldRead.GetILToLoad(context, processor));

            //Store to local
            ret.Add(processor.Create(OpCodes.Stloc, LocalWritten.Variable));
            
            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {_readFrom?.Name}.{FieldRead}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {FieldRead} from {_readFrom} and stores in a new local {LocalWritten} in {_destRegName}\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}