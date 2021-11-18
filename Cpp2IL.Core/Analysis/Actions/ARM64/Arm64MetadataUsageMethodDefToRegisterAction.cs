using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using LibCpp2IL.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64MetadataUsageMethodDefToRegisterAction : BaseAction<Arm64Instruction>
    {
        private MetadataUsage? _metadataUsage;
        private Il2CppTypeReflectionData? _type;
        private ulong _pointer;
        private ConstantDefinition? _constantMade;
        private string? _destReg;

        public Arm64MetadataUsageMethodDefToRegisterAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            long pageAddress;
            if (instruction.Details.Operands[1].Type == Arm64OperandType.Register && context.GetConstantInReg(Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[1].Register.Id)) is { Value: long pageAddr2 })
                pageAddress = pageAddr2;
            else if (instruction.MemoryBase() != null && context.GetConstantInReg(Arm64Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id)) is {Value: long pageAddress3})
                pageAddress = pageAddress3;
            else
                return;
             
            _pointer = (ulong) (pageAddress + (instruction.MemoryOperand() != null ? instruction.MemoryOffset() : instruction.Details.Operands[2].Immediate));
            _metadataUsage = LibCpp2IlMain.GetAnyGlobalByAddress(_pointer);
            
            if (_metadataUsage == null)
            {
                _pointer = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(_pointer);
                _metadataUsage = LibCpp2IlMain.GetAnyGlobalByAddress(_pointer);
            }
            
            if(_metadataUsage == null)
                return;

            _destReg = Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            
            _constantMade = context.MakeConstant(typeof(MethodDefinition), _metadataUsage.AsMethod().AsManaged(), reg: _destReg);
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