using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
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
            var globalAddress = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            var usage = LibCpp2IlMain.GetAnyGlobalByAddress(globalAddress);

            if(usage == null)
                return;

            try
            {
                _genericMethodRef = usage.AsGenericMethodRef();
            }
            catch (Exception)
            {
                Logger.WarnNewline($"Metadata usage at 0x{usage.Offset:X} of type generic method ref has invalid index {usage.RawValue} (0x{usage.RawValue:X})", "Analysis");
                return;
            }

            _declaringType = SharedState.UnmanagedToManagedTypes[_genericMethodRef.declaringType];
            _method = SharedState.UnmanagedToManagedMethods[_genericMethodRef.baseMethod];

            _genericTypeParams = _genericMethodRef.typeGenericParams.Select(data => Utils.TryResolveTypeReflectionData(data, _method)!).ToList();
            _genericMethodParams = _genericMethodRef.methodGenericParams.Select(data => Utils.TryResolveTypeReflectionData(data, _method)!).ToList();

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
            
            ConstantWritten = context.MakeConstant(typeof(GenericMethodReference), new GenericMethodReference(_declaringType, _method), name, destReg);
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
            return $"Loads the global generic method reference for method {_method} on type {_declaringType} and stores the result in constant {ConstantWritten}";
        }
    }
}