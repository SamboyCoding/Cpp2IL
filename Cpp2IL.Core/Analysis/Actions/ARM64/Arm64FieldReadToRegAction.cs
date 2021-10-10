using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64FieldReadToRegAction : AbstractFieldReadAction<Arm64Instruction>
    {
        public Arm64FieldReadToRegAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var sourceReg = instruction.MemoryBase()!.Id;
            ReadFrom = context.GetLocalInReg(Utils.GetRegisterNameNew(sourceReg));
            var destReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            if(ReadFrom?.Type == null)
                return;
            
            FieldRead = FieldUtils.GetFieldBeingAccessed(ReadFrom.Type, (ulong)instruction.MemoryOffset(), destReg[0] == 'v');
            
            if(FieldRead == null)
                return;
            
            RegisterUsedLocal(ReadFrom, context);

            LocalWritten = context.MakeLocal(FieldRead.GetFinalType()!, reg: destReg);
            
            RegisterUsedLocal(LocalWritten, context);
        }
    }
}