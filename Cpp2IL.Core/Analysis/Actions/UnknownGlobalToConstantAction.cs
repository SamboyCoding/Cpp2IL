using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class UnknownGlobalToConstantAction : BaseAction
    {
        private UnknownGlobalAddr _global;
        private string _destReg;
        private ConstantDefinition? _constantMade;

        public UnknownGlobalToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var offset = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;
            _global = new UnknownGlobalAddr(offset);
            
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            _constantMade = context.MakeConstant(typeof(UnknownGlobalAddr), _global, reg: _destReg);
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
            return $"Reads {_global} into register {_destReg} as a constant {_constantMade?.Name}";
        }
    }
}