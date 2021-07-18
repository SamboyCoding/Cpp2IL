using System;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
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

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (_theField == null || _localMade == null)
                throw new TaintedInstructionException("Couldn't identify the field being read (or its type).");

            if (_localMade.Variable == null)
                //Stripped out - no use found for the dest local.
                return Array.Empty<Mono.Cecil.Cil.Instruction>();
            
            return new[]
            {
                processor.Create(OpCodes.Ldsfld, _theField),
                processor.Create(OpCodes.Stloc, _localMade.Variable)
            };
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