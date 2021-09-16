using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class FieldToLocalAction : AbstractFieldReadAction<Instruction>
    {
        private string _destRegName;

        public FieldToLocalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var sourceRegName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destRegName = Utils.GetRegisterNameNew(instruction.Op0Register);
            var sourceFieldOffset = instruction.MemoryDisplacement;

            var readFrom = context.GetOperandInRegister(sourceRegName);

            TypeReference readFromType;
            if (readFrom is ConstantDefinition {Value: NewSafeCastResult<Instruction> result})
            {
                readFromType = result.castTo;
                ReadFrom = result.original;
                RegisterUsedLocal(ReadFrom);
            }
            else if(readFrom is LocalDefinition {IsMethodInfoParam: false} l && l.Type?.Resolve() != null)
            {
                ReadFrom = l;
                readFromType = ReadFrom!.Type!;
                RegisterUsedLocal(ReadFrom);
            } else
            {
                AddComment($"This shouldn't be a field read? Op in reg {sourceRegName} is {context.GetOperandInRegister(sourceRegName)}, offset is {sourceFieldOffset} (0x{sourceFieldOffset:X})");
                return;
            }

            FieldRead = FieldUtils.GetFieldBeingAccessed(readFromType, sourceFieldOffset, false);
            
            if(FieldRead == null) return;

            LocalWritten = context.MakeLocal(FieldRead.GetFinalType(), reg: _destRegName);
            RegisterDefinedLocalWithoutSideEffects(LocalWritten);
        }
    }
}