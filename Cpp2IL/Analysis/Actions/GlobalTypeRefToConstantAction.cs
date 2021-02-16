using System;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Iced.Intel;
using LibCpp2IL.Reflection;
using SharpDisasm.Udis86;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalTypeRefToConstantAction : BaseAction
    {
        public readonly TypeReference? ResolvedType;
        public readonly ConstantDefinition? ConstantWritten;
        private readonly string? _destReg;

        public GlobalTypeRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = LibCpp2IlMain.ThePe.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            var typeData = LibCpp2IlMain.GetTypeGlobalByAddress(globalAddress);

            if (typeData == null) return;

            ResolvedType = Utils.TryResolveTypeReflectionData(typeData);

            if (ResolvedType == null) return;

            _destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            var name = ResolvedType.Name;

            ConstantWritten = context.MakeConstant(typeof(TypeReference), ResolvedType, name, _destReg);
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
            return $"Loads the type definition for managed type {ResolvedType?.FullName} as a constant \"{ConstantWritten?.Name}\" in {_destReg}";
        }
    }
}