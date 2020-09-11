using System;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Iced.Intel;
using SharpDisasm.Udis86;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalTypeRefToConstantAction : BaseAction
    {
        public GlobalIdentifier GlobalRead;
        public TypeDefinition ResolvedType;
        public ConstantDefinition ConstantWritten;
        private string _destReg;

        public GlobalTypeRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = instruction.GetRipBasedInstructionMemoryAddress();
            var typeData = LibCpp2IlMain.GetTypeGlobalByAddress(globalAddress);
            var (type, genericParams) = Utils.TryLookupTypeDefByName(typeData!.ToString());
            ResolvedType = type;

            if (ResolvedType == null) return;
            
            _destReg = instruction.Op0Kind ==OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            var name = ResolvedType.Name;
            
            ConstantWritten = context.MakeConstant(typeof(TypeDefinition), ResolvedType, name, _destReg);
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
            return $"Loads the type definition for managed type {ResolvedType.FullName} as a constant \"{ConstantWritten.Name}\" in {_destReg}";
        }
    }
}