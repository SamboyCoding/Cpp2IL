﻿using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class Il2CppStringToConstantAction : BaseAction<Instruction>
    {
        private readonly string _detectedString;
        private string? _destReg;
        private ConstantDefinition? _constantMade;

        //This is specifically for UNMANAGED strings (i.e. those not specified in the metadata, such as names for ICall lookups, etc)
        public Il2CppStringToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction, string detectedString) : base(context, instruction)
        {
            _detectedString = detectedString;

            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            }

            _constantMade = context.MakeConstant(typeof(Il2CppString), new Il2CppString(_detectedString, instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64), reg: _destReg);
            
            if (instruction.Mnemonic == Mnemonic.Push)
            {
                context.Stack.Push(_constantMade);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads string \"{_detectedString}\" into register {_destReg} as constant {_constantMade}";
        }
    }
}