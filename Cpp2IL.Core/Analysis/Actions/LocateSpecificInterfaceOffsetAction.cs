using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class LocateSpecificInterfaceOffsetAction : BaseAction<Instruction>
    {
        private TypeDefinition _interfaceType;
        private InterfaceOffsetsReadAction offsetReads;
        public Il2CppInterfaceOffset? _matchingInterfaceOffset;

        public LocateSpecificInterfaceOffsetAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var secondOpName = Utils.GetRegisterNameNew(instruction.Op1Register);
            var secondOp = context.GetConstantInReg(secondOpName);
            _interfaceType = (TypeDefinition) secondOp.Value;

            offsetReads = (InterfaceOffsetsReadAction) context.Actions.Last(a => a is InterfaceOffsetsReadAction);
            
            _matchingInterfaceOffset = offsetReads.InterfaceOffsets.LastOrDefault(i => Utils.AreManagedAndCppTypesEqual(i.type, _interfaceType));
            
            if(_matchingInterfaceOffset == null)
                AddComment($"Warning: Could not find an interface offset for class {offsetReads.loadedFor.backingType.FullName}, where it implements interface {_interfaceType.FullName}.");
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
            return $"Checks for specific interface offset of type {_interfaceType.FullName} which resolves to offset {_matchingInterfaceOffset?.offset}";
        }
    }
}