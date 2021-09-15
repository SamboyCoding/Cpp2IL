using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class GlobalTypeRefToConstantAction : BaseAction<Instruction>
    {
        public readonly TypeReference? ResolvedType;
        public readonly ConstantDefinition? ConstantWritten;
        private readonly string? _destReg;

        public GlobalTypeRefToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;
            var typeData = LibCpp2IlMain.GetTypeGlobalByAddress(globalAddress);

            if (typeData == null) return;

            try
            {
                ResolvedType = Utils.TryResolveTypeReflectionData(typeData);
            }
            catch (ArgumentException)
            {
                Logger.WarnNewline($"Metadata usage at 0x{globalAddress:X} of type TypeDef specifies generic parameter {typeData}, which is invalid.", "Analysis");
            }

            if (ResolvedType == null) return;

            var name = ResolvedType.Name;
            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            }

            ConstantWritten = context.MakeConstant(typeof(TypeReference), ResolvedType, name, _destReg);
            
            if (instruction.Mnemonic == Mnemonic.Push)
            {
                context.Stack.Push(ConstantWritten);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
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