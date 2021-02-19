using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalMethodRefToConstantAction : BaseAction
    {
        private Il2CppGlobalGenericMethodRef? _genericMethodRef;
        private TypeReference? _declaringType;
        private MethodReference? _method;
        private List<TypeReference>? _genericTypeParams;
        private List<TypeReference>? _genericMethodParams;
        public ConstantDefinition? ConstantWritten;

        public GlobalMethodRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = LibCpp2IlMain.ThePe.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            var usage = LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress);

            if(usage == null)
                return;
            
            _genericMethodRef = usage.AsGenericMethodRef();

            _declaringType = SharedState.UnmanagedToManagedTypes[_genericMethodRef.declaringType];
            _method = SharedState.UnmanagedToManagedMethods[_genericMethodRef.baseMethod];

            _genericTypeParams = _genericMethodRef.typeGenericParams.Select(Utils.TryResolveTypeReflectionData).ToList();
            _genericMethodParams = _genericMethodRef.methodGenericParams.Select(Utils.TryResolveTypeReflectionData).ToList();

            if (_genericTypeParams.Count > 0)
            {
                _declaringType = _declaringType.MakeGenericInstanceType(_genericTypeParams.ToArray());
            }

            if (_genericMethodParams.Count > 0)
            {
                var gMethod = new GenericInstanceMethod(_method);
                _genericMethodParams.ForEach(gMethod.GenericArguments.Add);
                _method = gMethod;
            }

            var destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            var name = _method.Name;
            
            ConstantWritten = context.MakeConstant(typeof(MethodReference), _method, name, destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the global generic method reference for method {_method} on type {_declaringType} and stores the result in constant {ConstantWritten}";
        }
    }
}