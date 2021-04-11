using System;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class ClassPointerLoadAction : BaseAction
    {
        private readonly LocalDefinition? localCopiedFrom;
        private readonly ConstantDefinition? destinationConstant;
        private readonly string destReg;

        public ClassPointerLoadAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            if(instruction.Op0Register == Register.RSP)
                Console.WriteLine("WARNING: CLASS POINTER LOAD DEST IS STACK.");
            
            var sourceReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var inReg = context.GetOperandInRegister(sourceReg);
            localCopiedFrom = inReg is LocalDefinition local ? local : inReg is ConstantDefinition {Value: NewSafeCastResult result} ? result.original : null;

            if(localCopiedFrom == null)
                return;
            
            var localType = localCopiedFrom.Type?.Resolve();
            localType ??= Utils.ObjectReference;

            var cppTypeDef = SharedState.ManagedToUnmanagedTypes[localType];
            destinationConstant = context.MakeConstant(typeof(Il2CppClassIdentifier), new Il2CppClassIdentifier
            {
                backingType = cppTypeDef,
                objectAlias = localCopiedFrom.Name
            }, reg: destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
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