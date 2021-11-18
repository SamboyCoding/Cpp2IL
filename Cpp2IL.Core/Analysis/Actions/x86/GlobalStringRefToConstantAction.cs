using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class GlobalStringRefToConstantAction : BaseAction<Instruction>
    {
        public readonly string? ResolvedString;
        public ConstantDefinition? ConstantWritten;
        public LocalDefinition? LastKnownLocalInReg;
        private string? _destReg;

        public GlobalStringRefToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;

            try
            {
                ResolvedString = LibCpp2IlMain.GetLiteralByAddress(globalAddress);
            }
            catch (Exception)
            {
                var rawValue = LibCpp2IlMain.GetLiteralGlobalByAddress(globalAddress)!.RawValue;
                Logger.WarnNewline($"Metadata usage at 0x{globalAddress:X} of type string has invalid index {rawValue} (0x{rawValue:X})", "Analysis");
            }

            if (ResolvedString == null) return;

            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = instruction.Op0Kind == OpKind.Register ? X86Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            }

            LastKnownLocalInReg = context.GetLocalInReg(_destReg);
            ConstantWritten = context.MakeConstant(typeof(string), ResolvedString, null, _destReg);

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
            return $"Loads the string literal \"{ResolvedString}\" as a constant \"{ConstantWritten?.Name}\" in {_destReg}";
        }
    }
}