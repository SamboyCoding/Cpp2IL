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

        public FieldToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var sourceRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destRegName = Utils.GetRegisterNameNew(instruction.Op0Register);
            var sourceFieldOffset = instruction.MemoryDisplacement;

            var sourceLocal = context.GetLocalInReg(sourceRegName);
            
            if(sourceLocal?.Type?.Resolve() == null) return;

            var fields = SharedState.FieldsByType[sourceLocal.Type.Resolve()];
            
            if(fields == null) return;
            
            var fieldInType = fields.FirstOrDefault(f => f.Offset == sourceFieldOffset);

            if (fieldInType.Offset != sourceFieldOffset) return; //The "default" part of "FirstOrDefault"

            FieldRead = sourceLocal.Type.Resolve().Fields.FirstOrDefault(f => f.Name == fieldInType.Name);
            
            if(FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.FieldType, reg: _destRegName);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Reads field {FieldRead?.FullName} and stores in a new local {LocalWritten} in {_destRegName}";
        }
    }
}