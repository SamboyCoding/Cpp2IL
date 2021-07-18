using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class MethodDefiningTypeToConstantAction : BaseAction
    {
        private MethodReference? _methodBeingRead;
        private TypeReference? _declaringType;
        private ConstantDefinition? _constantMade;

        public MethodDefiningTypeToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var constantBeingRead = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (constantBeingRead?.Type != typeof(MethodReference))
                return;
            
            _methodBeingRead = constantBeingRead.Value as MethodReference;
            
            if(_methodBeingRead == null)
                return;

            _declaringType = _methodBeingRead.DeclaringType;

            _constantMade = context.MakeConstant(typeof(TypeReference), _declaringType, reg: Utils.GetRegisterNameNew(instruction.Op0Register));
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
            return $"Reads the declaring type for the method {_methodBeingRead} (which is {_declaringType}) and stores in constant {_constantMade}";
        }
    }
}