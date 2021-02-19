using System.Collections.Generic;
using System.Text;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallManagedFunctionInRegAction : BaseAction
    {
        private MethodDefinition? _targetMethod;
        private LocalDefinition? _instanceCalledOn;
        private List<IAnalysedOperand>? arguments;
        private LocalDefinition? _returnedLocal;

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
            
            if (_targetMethod?.ReturnType is { } returnType && returnType.FullName != "System.Void")
            {
                if (Utils.TryResolveType(returnType, out var returnDef))
                {
                    //Push return type to rax.
                    var destReg = Utils.ShouldBeInFloatingPointRegister(returnDef) ? "xmm0" : "rax";
                    _returnedLocal = context.MakeLocal(returnDef, reg: destReg);
                }
                else
                {
                    AddComment($"Failed to resolve return type {returnType} for pushing to rax.");
                }
            }

            if (!MethodUtils.CheckParameters(instruction, _targetMethod, context, !_targetMethod.IsStatic, out arguments, failOnLeftoverArgs: false))
            {
                AddComment("Mismatched parameters detected here.");
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        private IEnumerable<string> GetReadableArguments()
        {
            foreach (var arg in arguments)
            {
                if (arg is ConstantDefinition constantDefinition)
                    yield return constantDefinition.ToString();
                else
                    yield return ((LocalDefinition) arg).Name;
            }
        }
        
        public override string? ToPsuedoCode()
        {
            if (_targetMethod == null) return "[instruction error - managed method being called is null]";
            
            var ret = new StringBuilder();

            if (_returnedLocal != null)
                ret.Append(_returnedLocal?.Type?.FullName).Append(' ').Append(_returnedLocal?.Name).Append(" = ");

            if (_targetMethod.IsStatic)
                ret.Append(_targetMethod.DeclaringType.FullName);
            else
                ret.Append(_instanceCalledOn?.Name ?? "<ERRINSTANCE>");

            ret.Append('.').Append(_targetMethod?.Name).Append('(');

            if (arguments != null && arguments.Count > 0)
                ret.Append(string.Join(", ", GetReadableArguments()));

            ret.Append(')');

            return ret.ToString();
        }

        public override string ToTextSummary()
        {
            return $"[!] Calls method {_targetMethod.FullName} from a register, on instance {_instanceCalledOn} if applicable\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}