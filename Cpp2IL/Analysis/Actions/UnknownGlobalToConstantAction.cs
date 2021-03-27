using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class UnknownGlobalToConstantAction : BaseAction
    {
        private UnknownGlobalAddr _global;
        private string _destReg;

        public UnknownGlobalToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var offset = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            _global = new UnknownGlobalAddr(offset);
            
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            context.MakeConstant(typeof(UnknownGlobalAddr), _global, reg: _destReg);
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
            return $"Reads {_global} into register {_destReg}";
        }
    }
}