using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.PE;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallMethodSpecAction : BaseAction
    {
        private Il2CppMethodSpec? _methodSpec;
        private List<IAnalysedOperand>? _args;
        private LocalDefinition? _localMade;
        private bool IsVoid;

        public CallMethodSpecAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var methodSpecConst = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            _methodSpec = methodSpecConst?.Value as Il2CppMethodSpec;
            
            if(_methodSpec?.MethodDefinition == null)
                return;
            
            //todo params and return type
            if(!MethodUtils.CheckParameters(instruction, _methodSpec.MethodDefinition, context, !_methodSpec.MethodDefinition.IsStatic, out _args, null, false)) 
                AddComment("Parameter mismatch!");

            if (_methodSpec.MethodDefinition.ReturnType?.ToString() != "System.Void")
            {
                IsVoid = false;
                var returnType = Utils.TryResolveTypeReflectionData(_methodSpec.MethodDefinition.ReturnType);
                if (returnType != null)
                    _localMade = context.MakeLocal(returnType, reg: "rax");
            }
            else
            {
                IsVoid = true;
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            if (!IsVoid)
                return $"{_localMade?.Type} {_localMade?.Name} = {_methodSpec}({string.Join(", ", _args ?? new List<IAnalysedOperand>())})";
            
            return $"{_methodSpec}() //TODO Params and return type for method spec calls";
        }

        public override string ToTextSummary()
        {
            return $"Calls il2cpp method spec {_methodSpec} with parameters {_args?.ToStringEnumerable()}" + 
                   (IsVoid ? "" : $" and stores the result in new local {_localMade?.Name} in register rax");
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}