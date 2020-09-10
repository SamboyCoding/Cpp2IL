using System;
using Cpp2IL.Analysis.ResultModels;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL.Analysis.Actions
{
    public class ClassPointerLoadAction : BaseAction
    {
        private readonly LocalDefinition? localCopiedFrom;
        private readonly ConstantDefinition? destinationConstant;
        private readonly string destReg;

        public ClassPointerLoadAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            destReg = Utils.GetRegisterName(instruction.Operands[0]);
            if(instruction.Operands[0].Base == ud_type.UD_R_RSP)
                Console.WriteLine("WARNING: CLASS POINTER LOAD DEST IS STACK.");
            
            var sourceReg = Utils.GetRegisterName(instruction.Operands[1]);
            var inReg = context.GetOperandInRegister(sourceReg);
            localCopiedFrom = inReg is LocalDefinition local ? local : inReg is ConstantDefinition cons && cons.Value is NewSafeCastResult result ? result.original : null;
            if (localCopiedFrom == null) return;

            var cppTypeDef = SharedState.MonoToCppTypeDefs[localCopiedFrom.Type!];
            destinationConstant = context.MakeConstant(typeof(Il2CppClassIdentifier), new Il2CppClassIdentifier
            {
                backingType = cppTypeDef,
                objectAlias = localCopiedFrom.Name
            }, reg: destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
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