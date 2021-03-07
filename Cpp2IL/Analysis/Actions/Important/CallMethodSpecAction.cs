using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.PE;
using Mono.Cecil;
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
        private MethodReference? _managedMethod;

        public CallMethodSpecAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var methodSpecConst = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            _methodSpec = methodSpecConst?.Value as Il2CppMethodSpec;
            
            if(_methodSpec?.MethodDefinition == null)
                return;
            
            //todo params and return type
            if(!MethodUtils.CheckParameters(instruction, _methodSpec.MethodDefinition, context, !_methodSpec.MethodDefinition.IsStatic, out _args, null, false)) 
                AddComment("Parameter mismatch!");

            var instance = _methodSpec.MethodDefinition?.IsStatic == true ? null : context.GetLocalInReg("rcx");
            
            if(_methodSpec.MethodDefinition == null)
                return;
            
            _managedMethod = SharedState.UnmanagedToManagedMethods[_methodSpec.MethodDefinition];

            if (_methodSpec.MethodDefinition.ReturnType?.ToString() != "System.Void")
            {
                IsVoid = false;
                var returnType = Utils.TryResolveTypeReflectionData(_methodSpec.MethodDefinition.ReturnType);

                if (_methodSpec.classIndexIndex != -1)
                    _managedMethod = _managedMethod.MakeGeneric(_methodSpec.GenericClassParams.Select(p => Utils.TryResolveTypeReflectionData(p, _managedMethod)).ToArray()!);

                if (returnType is GenericInstanceType git)
                    returnType = GenericInstanceUtils.ResolveMethodGIT(git, _managedMethod, instance?.Type, _args?.Select(a => a is LocalDefinition l ? l.Type : null).ToArray() ?? System.Array.Empty<TypeReference>());
                
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
                return $"{_localMade?.Type} {_localMade?.Name} = {_managedMethod?.DeclaringType.FullName}.{_managedMethod?.Name}({string.Join(", ", _args ?? new List<IAnalysedOperand>())})";
            
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