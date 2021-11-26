using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class GlobalMethodRefToConstantAction : BaseAction<Instruction>
    {
        private Il2CppGenericMethodRef? _genericMethodRef;
        private TypeReference? _declaringType;
        private MethodReference? _method;
        private List<TypeReference>? _genericTypeParams;
        private List<TypeReference>? _genericMethodParams;
        public ConstantDefinition? ConstantWritten;
        private string? _destReg;

        public GlobalMethodRefToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = instruction.Op0Kind.IsImmediate() ? instruction.Immediate32 : instruction.MemoryDisplacement64;
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

            _declaringType = SharedState.UnmanagedToManagedTypes[_genericMethodRef.DeclaringType];
            _method = SharedState.UnmanagedToManagedMethods[_genericMethodRef.BaseMethod];

            _genericTypeParams = _genericMethodRef.TypeGenericParams.Select(data => MiscUtils.TryResolveTypeReflectionData(data, _method)!).ToList();
            _genericMethodParams = _genericMethodRef.MethodGenericParams.Select(data => MiscUtils.TryResolveTypeReflectionData(data, _method)!).ToList();

            if (_genericTypeParams.Count > 0)
            {
                _declaringType = _declaringType.MakeGenericInstanceType(_genericTypeParams.ToArray());
            }

            if (_genericMethodParams.Count > 0)
            {
                _method = _method.MakeGenericInstanceMethod(_genericMethodParams.ToArray());
            }

            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = instruction.Op0Kind == OpKind.Register ? X86Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            }

            var name = _method.Name;
            
            ConstantWritten = context.MakeConstant(typeof(GenericMethodReference), new GenericMethodReference(_declaringType, _method), name, _destReg);
            
            if (instruction.Mnemonic == Mnemonic.Push)
            {
                context.Stack.Push(ConstantWritten);
            }
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
            return $"Loads the global generic method reference for method {_method} on type {_declaringType} and stores the result in constant {ConstantWritten} in {_destReg}";
        }
    }
}