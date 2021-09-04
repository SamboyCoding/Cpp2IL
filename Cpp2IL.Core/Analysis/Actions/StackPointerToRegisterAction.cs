using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class StackPointerToRegisterAction : BaseAction<Instruction>
    {
        private string _destReg;
        private uint _stackOffset;
        private ConstantDefinition? _constantMade;

        public StackPointerToRegisterAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            _stackOffset = instruction.MemoryDisplacement32;

            _constantMade = context.MakeConstant(typeof(StackPointer), new StackPointer(_stackOffset), reg: _destReg);
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
            return $"Loads a pointer to stack offset 0x{_stackOffset:X} into register {_destReg} as new constant {_constantMade?.Name}";
        }
    }
}