using System;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class StaticFieldToRegAction : BaseAction
    {
        public readonly FieldDefinition? FieldRead;
        public readonly LocalDefinition? LocalWritten;
        private readonly string _destReg;

        public StaticFieldToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var fieldsPtrConst = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            if (fieldsPtrConst == null || fieldsPtrConst.Type != typeof(StaticFieldsPtr)) return;

            var fieldsPtr = (StaticFieldsPtr) fieldsPtrConst.Value;

            FieldRead = FieldUtils.GetStaticFieldByOffset(fieldsPtr, instruction.MemoryDisplacement);
            
            if (FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.FieldType, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (FieldRead == null || LocalWritten == null)
                throw new TaintedInstructionException("Couldn't identify the field being read (or its type).");

            if (LocalWritten.Variable == null)
                //Stripped out - no use found for the dest local.
                return Array.Empty<Mono.Cecil.Cil.Instruction>();
            
            return new[]
            {
                processor.Create(OpCodes.Ldsfld, processor.ImportReference(FieldRead)),
                processor.Create(OpCodes.Stloc, LocalWritten.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {FieldRead?.DeclaringType?.FullName}.{FieldRead?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads the static field {FieldRead?.FullName} into new local {LocalWritten?.Name} in register {_destReg}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}