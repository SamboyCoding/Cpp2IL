using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class RegToFieldAction : BaseAction
    {
        public readonly IAnalysedOperand? ValueRead;
        public readonly FieldUtils.FieldBeingAccessedData? FieldWritten;
        public readonly LocalDefinition? InstanceWrittenOn;

        //TODO: Fix string literal to field - it's a constant in a field.
        public RegToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var destRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement32;
            ValueRead = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.Op1Register));

            InstanceWrittenOn = context.GetLocalInReg(destRegName);
            
            if(ValueRead is LocalDefinition loc)
                RegisterUsedLocal(loc);

            if (InstanceWrittenOn?.Type?.Resolve() == null)
            {
                if (context.GetConstantInReg(destRegName) is {Value: FieldPointer p})
                {
                    InstanceWrittenOn = p.OnWhat;
                    RegisterUsedLocal(InstanceWrittenOn);
                    FieldWritten = p.Field;
                }
                
                return;
            }

            RegisterUsedLocal(InstanceWrittenOn);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceWrittenOn.Type, destFieldOffset, false);
        }

        internal RegToFieldAction(MethodAnalysis context, Instruction instruction, FieldUtils.FieldBeingAccessedData fieldWritten, LocalDefinition instanceWrittenOn, LocalDefinition readFrom) : base(context, instruction)
        {
            Debug.Assert(instanceWrittenOn.Type!.IsValueType);
            
            FieldWritten = fieldWritten;
            InstanceWrittenOn = instanceWrittenOn;
            ValueRead = readFrom;
            
            RegisterUsedLocal(InstanceWrittenOn);
            RegisterUsedLocal(readFrom);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (ValueRead == null || InstanceWrittenOn == null || FieldWritten == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            ret.AddRange(InstanceWrittenOn.GetILToLoad(context, processor));

            var f = FieldWritten;
            while (f.NextChainLink != null)
            {
                ret.Add(processor.Create(OpCodes.Ldfld, f.ImpliedFieldLoad));
                f = f.NextChainLink;
            }
            
            ret.AddRange(ValueRead.GetILToLoad(context, processor));

            if (f.FinalLoadInChain == null)
                throw new TaintedInstructionException("Final load in chain is null");
            
            ret.Add(processor.Create(OpCodes.Stfld, f.FinalLoadInChain));
            
            
            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{InstanceWrittenOn?.Name}.{FieldWritten} = {ValueRead?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets the field {FieldWritten} (Type {FieldWritten?.GetFinalType()}) on local {InstanceWrittenOn} to the value stored in {ValueRead}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}