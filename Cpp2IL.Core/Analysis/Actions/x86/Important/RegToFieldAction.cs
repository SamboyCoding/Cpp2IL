using System.Diagnostics;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class RegToFieldAction : AbstractFieldWriteFromVariableAction<Instruction>
    {
        //TODO: Fix string literal to field - it's a constant in a field.
        public RegToFieldAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var destRegName = X86Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destFieldOffset = instruction.MemoryDisplacement32;
            SourceOperand = context.GetOperandInRegister(X86Utils.GetRegisterNameNew(instruction.Op1Register));

            InstanceBeingSetOn = context.GetLocalInReg(destRegName);
            
            if(SourceOperand is LocalDefinition loc)
                RegisterUsedLocal(loc, context);

            if (SourceOperand is ConstantDefinition { Value: StackPointer s })
            {
                var offset = s.offset;
                if (context.StackStoredLocals.TryGetValue((int)offset, out var tempLocal))
                    SourceOperand = tempLocal;
                else
                    SourceOperand = context.EmptyRegConstant;
            }

            if (InstanceBeingSetOn?.Type?.Resolve() == null)
            {
                if (context.GetConstantInReg(destRegName) is {Value: FieldPointer p})
                {
                    InstanceBeingSetOn = p.OnWhat;
                    RegisterUsedLocal(InstanceBeingSetOn, context);
                    FieldWritten = p.Field;
                }
                
                return;
            }

            RegisterUsedLocal(InstanceBeingSetOn, context);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, destFieldOffset, false);
        }

        internal RegToFieldAction(MethodAnalysis<Instruction> context, Instruction instruction, FieldUtils.FieldBeingAccessedData fieldWritten, LocalDefinition instanceWrittenOn, LocalDefinition readFrom) : base(context, instruction)
        {
            Debug.Assert(instanceWrittenOn.Type!.IsValueType);
            
            FieldWritten = fieldWritten;
            InstanceBeingSetOn = instanceWrittenOn;
            SourceOperand = readFrom;
            
            RegisterUsedLocal(InstanceBeingSetOn, context);
            RegisterUsedLocal(readFrom, context);
        }

        protected override string? GetValuePseudocode() => SourceOperand?.GetPseudocodeRepresentation();

        protected override string? GetValueSummary() => SourceOperand?.ToString();
    }
}