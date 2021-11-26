using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class FieldToLocalAction : AbstractFieldReadAction<Instruction>
    {
        private string _destRegName;

        public FieldToLocalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var sourceRegName = X86Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destRegName = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            var sourceFieldOffset = instruction.MemoryDisplacement32;

            var readFrom = context.GetOperandInRegister(sourceRegName);

            if (readFrom is ConstantDefinition {Value: NewSafeCastResult<Instruction> result})
            {
                ReadFromType = result.castTo;
                ReadFrom = result.original;
                RegisterUsedLocal(ReadFrom, context);
            }
            else if(readFrom is LocalDefinition {IsMethodInfoParam: false} l && l.Type?.Resolve() != null)
            {
                ReadFrom = l;
                ReadFromType = ReadFrom!.Type!;
                RegisterUsedLocal(ReadFrom, context);
            } else
            {
                AddComment($"This shouldn't be a field read? Op in reg {sourceRegName} is {context.GetOperandInRegister(sourceRegName)}, offset is {sourceFieldOffset} (0x{sourceFieldOffset:X})");
                return;
            }

            FieldRead = FieldUtils.GetFieldBeingAccessed(ReadFromType, sourceFieldOffset, false);
            
            if(FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.GetFinalType(), reg: _destRegName);
            FixUpFieldRefForAnyPotentialGenericType(context);
            RegisterDefinedLocalWithoutSideEffects(LocalWritten);
        }
    }
}