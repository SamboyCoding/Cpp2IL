using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class InterfaceOffsetCountToLocalAction : BaseAction
    {
        private ushort offsetCount;
        private LocalDefinition? _localMade;
        private Il2CppTypeDefinition? _typeCountReadFrom;

        public InterfaceOffsetCountToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var constantBeingRead = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (constantBeingRead?.Type != typeof(Il2CppClassIdentifier))
                return;

            _typeCountReadFrom = (constantBeingRead.Value as Il2CppClassIdentifier)?.backingType;

            if (_typeCountReadFrom == null)
                return;

            offsetCount = _typeCountReadFrom?.interface_offsets_count ?? 0;

            if (offsetCount != 0)
                _localMade = context.MakeLocal(Utils.UInt32Reference, reg: Utils.GetRegisterNameNew(instruction.Op0Register), knownInitialValue: offsetCount);
            else
                _localMade = context.MakeLocal(Utils.UInt32Reference, reg: Utils.GetRegisterNameNew(instruction.Op0Register));
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
            return $"Reads the number of interface offsets for type {_typeCountReadFrom} (which is {offsetCount}) and stores in new local {_localMade}";
        }
    }
}