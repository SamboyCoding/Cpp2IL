using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ImmediateToFieldAction: AbstractFieldWriteAction
    {
        public object ConstantValue;
        
        public ImmediateToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (ConstantValue == null || InstanceBeingSetOn == null || FieldWritten == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            ret.AddRange(InstanceBeingSetOn.GetILToLoad(context, processor));

            var f = FieldWritten;
            while (f.NextChainLink != null)
            {
                ret.Add(processor.Create(OpCodes.Ldfld, processor.ImportReference(f.ImpliedFieldLoad!)));
                f = f.NextChainLink;
            }

            ret.AddRange(context.MakeConstant(ConstantValue.GetType(), ConstantValue).GetILToLoad(context, processor));

            if (f.FinalLoadInChain == null)
                throw new TaintedInstructionException("Final load in chain is null");
            
            ret.Add(processor.Create(OpCodes.Stfld, processor.ImportReference(f.FinalLoadInChain)));
            
            
            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{InstanceBeingSetOn?.GetPseudocodeRepresentation()}.{FieldWritten} = {ConstantValue}";
        }

        public override string ToTextSummary()
        {
            if(ConstantValue is float || ConstantValue is double)
                return $"[!] Writes the floating-point constant {ConstantValue} into the field {FieldWritten} of {InstanceBeingSetOn}";
            
            return $"[!] Writes the constant {ConstantValue} (0x{ConstantValue:X}) into the field {FieldWritten} of {InstanceBeingSetOn}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}