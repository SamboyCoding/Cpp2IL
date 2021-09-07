using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class LoadConstantUsingLeaAction : BaseAction<Instruction>
    {
        private readonly uint _amount;
        private readonly string? _destReg;
        private readonly ConstantDefinition _constantMade;

        public LoadConstantUsingLeaAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _amount = instruction.MemoryDisplacement32;
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            _constantMade = context.MakeConstant(typeof(int), (int) _amount, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            return Array.Empty<Mono.Cecil.Cil.Instruction>();
        }

        public override string ToPsuedoCode()
        {
            return $"System.Int32 {_constantMade.GetPseudocodeRepresentation()} = {_amount}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Loads the constant value {_amount} into the register {_destReg} as constant {_constantMade.Name} using an LEA instruction";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}