using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ImmediateToFieldAction: AbstractFieldWriteAction<Instruction>
    {
        public object ConstantValue;
        
        public ImmediateToFieldAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var rawConstant = instruction.GetImmediate(1);

            ConstantValue = rawConstant;
            
            var destRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement32;

            InstanceBeingSetOn = context.GetLocalInReg(destRegName);
            
            if(InstanceBeingSetOn?.Type?.Resolve() == null) return;

            RegisterUsedLocal(InstanceBeingSetOn);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, destFieldOffset, false);

            var destTypeName = FieldWritten?.GetFinalType()?.FullName;
            if (destTypeName == "System.Single")
                ConstantValue = BitConverter.ToSingle(BitConverter.GetBytes(rawConstant), 0);
            else if(destTypeName == "System.Double")
                ConstantValue = BitConverter.ToDouble(BitConverter.GetBytes(rawConstant), 0);
        }

        protected override string? GetValueSummary() => ConstantValue?.ToString();

        protected override string? GetValuePseudocode() => ConstantValue?.ToString();

        protected override Mono.Cecil.Cil.Instruction[] GetIlToLoadValue(MethodAnalysis<Instruction> context, ILProcessor processor) => context.MakeConstant(ConstantValue.GetType(), ConstantValue).GetILToLoad(context, processor);
    }
}