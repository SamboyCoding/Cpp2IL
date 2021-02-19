using System.Diagnostics;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class LocalToFieldAction : BaseAction
    {
        public readonly LocalDefinition? LocalRead;
        public readonly FieldUtils.FieldBeingAccessedData? FieldWritten;
        private readonly LocalDefinition? _writtenOn;

        public LocalToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var destRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement;
            LocalRead = context.GetLocalInReg(Utils.GetRegisterNameNew(instruction.Op1Register));

            _writtenOn = context.GetLocalInReg(destRegName);
            
            if(_writtenOn?.Type?.Resolve() == null) return;

            FieldWritten = FieldUtils.GetFieldBeingAccessed(_writtenOn.Type, destFieldOffset, false);
        }

        internal LocalToFieldAction(MethodAnalysis context, Instruction instruction, FieldUtils.FieldBeingAccessedData fieldWritten, LocalDefinition writtenOn, LocalDefinition readFrom) : base(context, instruction)
        {
            Debug.Assert(writtenOn.Type!.IsValueType);
            
            FieldWritten = fieldWritten;
            _writtenOn = writtenOn;
            LocalRead = readFrom;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"{_writtenOn?.Name}.{FieldWritten} = {LocalRead?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets the field {FieldWritten} on local {_writtenOn} to the value stored in {LocalRead}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}