using System;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class FieldToLocalAction : BaseAction
    {
        public FieldDefinition? FieldRead;
        public LocalDefinition? LocalWritten;
        private string _destRegName;
        private LocalDefinition? _readFrom;

        public FieldToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var sourceRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destRegName = Utils.GetRegisterNameNew(instruction.Op0Register);
            var sourceFieldOffset = instruction.MemoryDisplacement;

            _readFrom = context.GetLocalInReg(sourceRegName);
            
            if(_readFrom?.Type?.Resolve() == null) return;

            var fields = SharedState.FieldsByType[_readFrom.Type.Resolve()];
            
            if(fields == null) return;
            
            var fieldInType = fields.FirstOrDefault(f => f.Offset == sourceFieldOffset);

            if (fieldInType.Offset != sourceFieldOffset) return; //The "default" part of "FirstOrDefault"

            FieldRead = _readFrom.Type.Resolve().Fields.FirstOrDefault(f => f.Name == fieldInType.Name);
            
            if(FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.FieldType, reg: _destRegName);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {_readFrom?.Name}.{FieldRead?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {FieldRead?.FullName} from {_readFrom} and stores in a new local {LocalWritten} in {_destRegName}\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}