﻿using System;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalStringRefToConstantAction : BaseAction
    {
        public readonly string? ResolvedString;
        public readonly ConstantDefinition? ConstantWritten;
        private string? _destReg;

        public GlobalStringRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();

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
            
            _destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            
            ConstantWritten = context.MakeConstant(typeof(string), ResolvedString, null, _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
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