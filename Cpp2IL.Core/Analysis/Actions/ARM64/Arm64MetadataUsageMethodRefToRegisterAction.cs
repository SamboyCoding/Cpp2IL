using System;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Mono.Cecil.Cil.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64MetadataUsageMethodRefToRegisterAction : BaseAction<Arm64Instruction>
    {
        private MetadataUsage? _metadataUsage;
        private Il2CppTypeReflectionData? _type;
        private ulong _pointer;
        private ConstantDefinition? _constantMade;
        private string? _destReg;
        private readonly Il2CppGlobalGenericMethodRef _genericMethodRef;

        public Arm64MetadataUsageMethodRefToRegisterAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            if (context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id)) is not { Value: long pageAddress })
                return;

            _pointer = (ulong)(pageAddress + instruction.MemoryOffset());
            _metadataUsage = LibCpp2IlMain.GetAnyGlobalByAddress(_pointer);
            
            if (_metadataUsage == null)
            {
                _pointer = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(_pointer);
                _metadataUsage = LibCpp2IlMain.GetAnyGlobalByAddress(_pointer);
            }

            if (_metadataUsage == null)
                return;

            _destReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            try
            {
                _genericMethodRef = _metadataUsage.AsGenericMethodRef();
            }
            catch (Exception)
            {
                Logger.WarnNewline($"Metadata usage at 0x{_metadataUsage.Offset:X} of type generic method ref has invalid index {_metadataUsage.RawValue} (0x{_metadataUsage.RawValue:X})", "Analysis");
                return;
            }

            TypeReference declaringType = SharedState.UnmanagedToManagedTypes[_genericMethodRef.declaringType];
            MethodReference method = SharedState.UnmanagedToManagedMethods[_genericMethodRef.baseMethod];

            var genericTypeParams = _genericMethodRef.typeGenericParams.Select(data => Utils.TryResolveTypeReflectionData(data, method)!).ToList();
            var genericMethodParams = _genericMethodRef.methodGenericParams.Select(data => Utils.TryResolveTypeReflectionData(data, method)!).ToList();

            if (genericTypeParams.Count > 0)
            {
                declaringType = declaringType.MakeGenericInstanceType(genericTypeParams.ToArray());
            }

            if (genericMethodParams.Count > 0)
            {
                var gMethod = new GenericInstanceMethod(method);
                genericMethodParams.ForEach(gMethod.GenericArguments.Add);
                method = gMethod;
            }

            var name = method.Name;

            _constantMade = context.MakeConstant(typeof(GenericMethodReference), new GenericMethodReference(declaringType, method), name, _destReg);
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the metadata usage {_metadataUsage} from global address 0x{_pointer:X} and stores it in {_destReg} as new constant {_constantMade}";
        }
    }
}