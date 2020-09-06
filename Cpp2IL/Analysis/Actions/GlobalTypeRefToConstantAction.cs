using System;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalTypeRefToConstantAction : BaseAction
    {
        public GlobalIdentifier GlobalRead;
        public TypeDefinition ResolvedType;
        public ConstantDefinition ConstantWritten;
        
        public GlobalTypeRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = context.MethodStart + Utils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]);
            GlobalRead = SharedState.GlobalsByOffset[globalAddress];
            var (type, genericParams) = Utils.TryLookupTypeDefByName(GlobalRead.Name);
            ResolvedType = type;

            if (ResolvedType == null) return;
            
            var destReg = instruction.Operands[0].Type == ud_type.UD_OP_REG ? Utils.GetRegisterName(instruction.Operands[0]) : null;
            var name = ResolvedType.Name;
            
            ConstantWritten = context.MakeConstant(typeof(TypeDefinition), ResolvedType, name, destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the type definition for managed type {ResolvedType?.FullName} as a constant \"{ConstantWritten.Name}\"";
        }
    }
}