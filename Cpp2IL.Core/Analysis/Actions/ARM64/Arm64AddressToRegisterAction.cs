using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64AddressToRegisterAction : BaseAction<Arm64Instruction>
    {
        private long _addressLoaded;
        private string _destReg;
        private ConstantDefinition _constantMade;
        
        public Arm64AddressToRegisterAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _addressLoaded = instruction.Details.Operands[1].Immediate;
            _destReg = Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            _constantMade = context.MakeConstant(typeof(long), _addressLoaded, reg: _destReg);
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the address 0x{_addressLoaded:X} into register {_destReg} as constant {_constantMade}";
        }
    }
}