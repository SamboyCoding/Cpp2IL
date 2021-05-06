using System;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class ConstantToFieldAction: BaseAction
    {
        private object constantValue;
        private LocalDefinition? instance;
        private FieldUtils.FieldBeingAccessedData? destinationField;
        
        public ConstantToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var rawConstant = instruction.GetImmediate(1);

            constantValue = rawConstant;
            
            var destRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement32;

            instance = context.GetLocalInReg(destRegName);
            
            if(instance?.Type?.Resolve() == null) return;

            RegisterUsedLocal(instance);

            destinationField = FieldUtils.GetFieldBeingAccessed(instance.Type, destFieldOffset, false);

            var destTypeName = destinationField?.GetFinalType()?.FullName;
            if (destTypeName == "System.Single")
                constantValue = BitConverter.ToSingle(BitConverter.GetBytes(rawConstant));
            else if(destTypeName == "System.Double")
                constantValue = BitConverter.ToDouble(BitConverter.GetBytes(rawConstant));
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"{instance?.GetPseudocodeRepresentation()}.{destinationField} = {constantValue}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes the constant {constantValue} into the field {destinationField} of {instance}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}