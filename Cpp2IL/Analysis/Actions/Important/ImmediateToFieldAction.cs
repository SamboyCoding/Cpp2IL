using System;
using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class ImmediateToFieldAction: BaseAction
    {
        private object constantValue;
        private LocalDefinition? instance;
        private FieldUtils.FieldBeingAccessedData? destinationField;
        
        public ImmediateToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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
            if (constantValue == null || instance == null || destinationField == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            ret.AddRange(instance.GetILToLoad(context, processor));

            var f = destinationField;
            while (f.NextChainLink != null)
            {
                ret.Add(processor.Create(OpCodes.Ldfld, f.ImpliedFieldLoad));
                f = f.NextChainLink;
            }

            ret.AddRange(context.MakeConstant(constantValue.GetType(), constantValue).GetILToLoad(context, processor));

            if (f.FinalLoadInChain == null)
                throw new TaintedInstructionException("Final load in chain is null");
            
            ret.Add(processor.Create(OpCodes.Stfld, f.FinalLoadInChain));
            
            
            return ret.ToArray();
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