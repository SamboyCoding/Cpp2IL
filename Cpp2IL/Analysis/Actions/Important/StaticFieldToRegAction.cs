using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class StaticFieldToRegAction : BaseAction
    {
        private readonly FieldDefinition? _theField;
        private readonly string _destReg;
        private readonly LocalDefinition? _localMade;

        public StaticFieldToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var fieldsPtrConst = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            if (fieldsPtrConst == null || fieldsPtrConst.Type != typeof(StaticFieldsPtr)) return;

            var fieldsPtr = (StaticFieldsPtr) fieldsPtrConst.Value;

            _theField = FieldUtils.GetStaticFieldByOffset(fieldsPtr, instruction.MemoryDisplacement);
            
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