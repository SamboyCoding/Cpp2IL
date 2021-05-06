using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class RegToFieldAction : BaseAction
    {
        public readonly IAnalysedOperand? ValueRead;
        public readonly FieldUtils.FieldBeingAccessedData? FieldWritten;
        private readonly LocalDefinition? _writtenOn;

        //TODO: Fix string literal to field - it's a constant in a field.
        public RegToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var destRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement32;
            ValueRead = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.Op1Register));

            _writtenOn = context.GetLocalInReg(destRegName);
            
            if(_writtenOn?.Type?.Resolve() == null) return;
            
            if(ValueRead is LocalDefinition loc)
                RegisterUsedLocal(loc);
            
            RegisterUsedLocal(_writtenOn);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(_writtenOn.Type, destFieldOffset, false);
        }

        internal RegToFieldAction(MethodAnalysis context, Instruction instruction, FieldUtils.FieldBeingAccessedData fieldWritten, LocalDefinition writtenOn, LocalDefinition readFrom) : base(context, instruction)
        {
            Debug.Assert(writtenOn.Type!.IsValueType);
            
            FieldWritten = fieldWritten;
            _writtenOn = writtenOn;
            ValueRead = readFrom;
            
            RegisterUsedLocal(_writtenOn);
            RegisterUsedLocal(readFrom);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (ValueRead == null || _writtenOn == null || FieldWritten == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            ret.AddRange(_writtenOn.GetILToLoad(context, processor));

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
            return $"{_writtenOn?.Name}.{FieldWritten} = {ValueRead?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets the field {FieldWritten} (Type {FieldWritten?.GetFinalType()}) on local {_writtenOn} to the value stored in {ValueRead}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}