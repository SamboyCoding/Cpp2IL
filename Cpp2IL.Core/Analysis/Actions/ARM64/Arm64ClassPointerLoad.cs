using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ClassPointerLoadAction : BaseAction<Arm64Instruction>
    {
        private readonly LocalDefinition? localCopiedFrom;
        private readonly ConstantDefinition? destinationConstant;
        private readonly string destReg;

        public Arm64ClassPointerLoadAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var op0 = instruction.Details.Operands[0].Register;
            destReg = Utils.GetRegisterNameNew(op0.Id);

            var sourceReg = Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id);
            var inReg = context.GetOperandInRegister(sourceReg);
            localCopiedFrom = inReg is LocalDefinition local ? local : inReg is ConstantDefinition { Value: NewSafeCastResult<Arm64Instruction> result } ? result.original : null;

            if (localCopiedFrom == null)
                return;

            var localType = localCopiedFrom.Type?.Resolve();
            localType ??= Utils.ObjectReference;

            if (!SharedState.ManagedToUnmanagedTypes.TryGetValue(localType, out var cppTypeDef))
                return;

            destinationConstant = context.MakeConstant(typeof(Il2CppClassIdentifier), new Il2CppClassIdentifier
            {
                backingType = cppTypeDef,
                objectAlias = localCopiedFrom.Name
            }, reg: destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the class pointer from {localCopiedFrom?.Name} into a constant {destinationConstant?.Name} in register {destReg}";
        }
    }
}