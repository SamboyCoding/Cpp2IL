using System.Collections.Generic;
using System.Diagnostics;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class RegToFieldAction : AbstractFieldWriteAction<Instruction>
    {
        public readonly IAnalysedOperand<Instruction>? ValueRead;

        //TODO: Fix string literal to field - it's a constant in a field.
        public RegToFieldAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var destRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement32;
            ValueRead = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.Op1Register));

            InstanceBeingSetOn = context.GetLocalInReg(destRegName);
            
            if(ValueRead is LocalDefinition<Instruction> loc)
                RegisterUsedLocal(loc);

            if (ValueRead is ConstantDefinition<Instruction> { Value: StackPointer s })
            {
                var offset = s.offset;
                if (context.StackStoredLocals.TryGetValue((int)offset, out var tempLocal))
                    ValueRead = tempLocal;
                else
                    ValueRead = context.EmptyRegConstant;
            }

            if (InstanceBeingSetOn?.Type?.Resolve() == null)
            {
                if (context.GetConstantInReg(destRegName) is {Value: FieldPointer<Instruction> p})
                {
                    InstanceBeingSetOn = p.OnWhat;
                    RegisterUsedLocal(InstanceBeingSetOn);
                    FieldWritten = p.Field;
                }
                
                return;
            }

            RegisterUsedLocal(InstanceBeingSetOn);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, destFieldOffset, false);
        }

        internal RegToFieldAction(MethodAnalysis<Instruction> context, Instruction instruction, FieldUtils.FieldBeingAccessedData fieldWritten, LocalDefinition<Instruction> instanceWrittenOn, LocalDefinition<Instruction> readFrom) : base(context, instruction)
        {
            Debug.Assert(instanceWrittenOn.Type!.IsValueType);
            
            FieldWritten = fieldWritten;
            InstanceBeingSetOn = instanceWrittenOn;
            ValueRead = readFrom;
            
            RegisterUsedLocal(InstanceBeingSetOn);
            RegisterUsedLocal(readFrom);
        }

        protected override string? GetValuePseudocode() => ValueRead?.GetPseudocodeRepresentation();

        protected override string? GetValueSummary() => ValueRead?.ToString();

        protected override Mono.Cecil.Cil.Instruction[] GetIlToLoadValue(MethodAnalysis<Instruction> context, ILProcessor processor) => ValueRead?.GetILToLoad(context, processor) ?? throw new TaintedInstructionException("Value read is null");
    }
}