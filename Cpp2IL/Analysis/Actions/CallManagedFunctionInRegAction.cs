using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class CallManagedFunctionInRegAction : BaseAction
    {
        private MethodDefinition _targetMethod;
        private LocalDefinition? _instanceCalledOn;

        public CallManagedFunctionInRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var regName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var operand = context.GetConstantInReg(regName);
            _targetMethod = (MethodDefinition) operand.Value;

            if (!_targetMethod.IsStatic)
            {
                _instanceCalledOn = context.GetLocalInReg("rcx");
                if (_instanceCalledOn == null)
                {
                    var cons = context.GetConstantInReg("rcx");
                    if (cons?.Value is NewSafeCastResult castResult)
                        _instanceCalledOn = castResult.original;
                }
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"[!] Calls method {_targetMethod.FullName} from a register, on instance {_instanceCalledOn} if applicable\n";
        }
    }
}