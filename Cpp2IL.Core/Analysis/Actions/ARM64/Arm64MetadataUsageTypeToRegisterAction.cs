using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using LibCpp2IL.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64MetadataUsageTypeToRegisterAction : BaseAction<Arm64Instruction>
    {
        private MetadataUsage? _metadataUsage;
        private Il2CppTypeReflectionData? _type;
        private ulong _pointer;
        private ConstantDefinition? _constantMade;
        private string? _destReg;

        public Arm64MetadataUsageTypeToRegisterAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            if(context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id)) is not {Value: long pageAddress})
                return;
             
            _pointer = (ulong) (pageAddress + instruction.MemoryOffset());
            _metadataUsage = LibCpp2IlMain.GetAnyGlobalByAddress(_pointer);

            if (_metadataUsage == null)
            {
                _pointer = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(_pointer);
                _metadataUsage = LibCpp2IlMain.GetAnyGlobalByAddress(_pointer);
            }
            
            if(_metadataUsage == null)
                return;

            _destReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            var resolved = Utils.TryResolveTypeReflectionData(_metadataUsage.AsType());
            
            if(resolved == null)
                return;
            
            _constantMade = context.MakeConstant(typeof(TypeReference), resolved, reg: _destReg);
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