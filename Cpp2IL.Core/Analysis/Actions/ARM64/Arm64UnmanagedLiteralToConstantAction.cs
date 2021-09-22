using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64UnmanagedLiteralToConstantAction : BaseAction<Arm64Instruction>
    {
        private readonly ConstantDefinition? _constantMade;
        public readonly Il2CppString? Il2CppString;

        public Arm64UnmanagedLiteralToConstantAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, string literal, ulong address) : base(context, instruction)
        {
            var destReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].RegisterSafe()?.Id ?? Arm64RegisterId.Invalid);
            
            if(string.IsNullOrEmpty(destReg))
                return;

            Il2CppString = new(literal, address);
            _constantMade = context.MakeConstant(typeof(Il2CppString), Il2CppString, reg: destReg);
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
            return $"Loads il2cpp string {Il2CppString} into new constant {_constantMade}";
        }
    }
}