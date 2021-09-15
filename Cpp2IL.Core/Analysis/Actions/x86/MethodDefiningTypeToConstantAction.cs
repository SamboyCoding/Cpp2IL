using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class MethodDefiningTypeToConstantAction : BaseAction<Instruction>
    {
        private MethodReference? _methodBeingRead;
        private TypeReference? _declaringType;
        private ConstantDefinition? _constantMade;

        public MethodDefiningTypeToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var constantBeingRead = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (constantBeingRead?.Type != typeof(MethodReference))
            {
                if (constantBeingRead?.Value is GenericMethodReference gmr)
                    _methodBeingRead = gmr.Method;
                else
                    return;
            }
            else
            {
                _methodBeingRead = constantBeingRead.Value as MethodReference;
            }
            
            if(_methodBeingRead == null)
                return;

            _declaringType = _methodBeingRead.DeclaringType;

            _constantMade = context.MakeConstant(typeof(TypeReference), _declaringType, reg: Utils.GetRegisterNameNew(instruction.Op0Register));
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
            return $"Reads the declaring type for the method {_methodBeingRead} (which is {_declaringType}) and stores in constant {_constantMade}";
        }
    }
}