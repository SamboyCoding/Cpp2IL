using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class StaticFieldToRegAction : BaseAction
    {
        private readonly TypeDefinition? _theType;
        private readonly uint _fieldOffset;
        private readonly FieldDefinition? _theField;
        private readonly string _destReg;
        private readonly LocalDefinition? _localMade;

        public StaticFieldToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var fieldsPtrConst = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            if (fieldsPtrConst == null || fieldsPtrConst.Type != typeof(StaticFieldsPtr)) return;

            var fieldsPtr = (StaticFieldsPtr) fieldsPtrConst.Value;

            _theType = fieldsPtr.TypeTheseFieldsAreFor;

            _fieldOffset = instruction.MemoryDisplacement;

            var theFields = SharedState.FieldsByType[_theType];
            var fieldName = theFields.SingleOrDefault(f => f.Static && f.Constant == null && f.Offset == _fieldOffset).Name;
            
            if(string.IsNullOrEmpty(fieldName)) return;

            _theField = _theType.Fields.FirstOrDefault(f => f.IsStatic && f.Name == fieldName);

            if (_theField == null) return;

            _localMade = context.MakeLocal(_theField.FieldType, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type?.FullName} {_localMade?.Name} = {_theField?.DeclaringType?.FullName}.{_theField?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads the static field {_theField?.FullName} into new local {_localMade?.Name} in register {_destReg}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}