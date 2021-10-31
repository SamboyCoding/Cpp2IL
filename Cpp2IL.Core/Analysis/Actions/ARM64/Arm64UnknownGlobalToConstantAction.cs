using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64UnknownGlobalToConstantAction : BaseAction<Arm64Instruction>
    {
        private UnknownGlobalAddr? _globalAddr;
        private ConstantDefinition? _constantMade;

        public Arm64UnknownGlobalToConstantAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, ulong globalAddress) : base(context, instruction)
        {
            var destReg = Utils.Utils.GetRegisterNameNew(instruction.Details.Operands[0].RegisterSafe()?.Id ?? Arm64RegisterId.Invalid);
            
            if(string.IsNullOrEmpty(destReg))
                return;

            _globalAddr = new(globalAddress);
            _constantMade = context.MakeConstant(typeof(Il2CppString), _globalAddr, reg: destReg);
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
            return $"Loads {_globalAddr} into new constant {_constantMade}";
        }
    }
}