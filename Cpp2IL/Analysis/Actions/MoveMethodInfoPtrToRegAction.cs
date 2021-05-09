using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class MoveMethodInfoPtrToRegAction : BaseAction
    {
        private readonly MethodReference? _methodBeingRead;
        private readonly string? _destReg;
        private string? _sourceReg;

        public MoveMethodInfoPtrToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _sourceReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var constantBeingRead = context.GetConstantInReg(_sourceReg);

            if (constantBeingRead?.Type != typeof(MethodReference))
                return;
            
            _methodBeingRead = constantBeingRead.Value as MethodReference;
            
            if(_methodBeingRead == null)
                return;

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            
            context.SetRegContent(_destReg, constantBeingRead);
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
            return $"Moves the method pointer for method {_methodBeingRead} from {_sourceReg} to {_destReg}";
        }
    }
}