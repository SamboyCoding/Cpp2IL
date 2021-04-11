using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

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
            
            if(LocalRead != null)
                RegisterUsedLocal(LocalRead);
            
            RegisterUsedLocal(_writtenOn);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(_writtenOn.Type, destFieldOffset, false);
        }

        internal LocalToFieldAction(MethodAnalysis context, Instruction instruction, FieldUtils.FieldBeingAccessedData fieldWritten, LocalDefinition writtenOn, LocalDefinition readFrom) : base(context, instruction)
        {
            Debug.Assert(writtenOn.Type!.IsValueType);
            
            FieldWritten = fieldWritten;
            _writtenOn = writtenOn;
            LocalRead = readFrom;
            
            RegisterUsedLocal(_writtenOn);
            RegisterUsedLocal(LocalRead);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (LocalRead == null || _writtenOn == null || FieldWritten == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            ret.AddRange(_writtenOn.GetILToLoad(context, processor));

            var f = FieldWritten;
            while (f.NextChainLink != null)
            {
                ret.Add(processor.Create(OpCodes.Ldfld, f.ImpliedFieldLoad));
                f = f.NextChainLink;
            }
            
            ret.AddRange(LocalRead.GetILToLoad(context, processor));

            if (f.FinalLoadInChain == null)
                throw new TaintedInstructionException("Final load in chain is null");
            
            ret.Add(processor.Create(OpCodes.Stfld, f.FinalLoadInChain));
            
            
            return ret.ToArray();
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