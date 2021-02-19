using System;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LocateSpecificInterfaceOffsetAction : BaseAction
    {
        private TypeDefinition _interfaceType;
        private InterfaceOffsetsReadAction offsetReads;
        public Il2CppInterfaceOffset _matchingInterfaceOffset;

        public LocateSpecificInterfaceOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var secondOpName = Utils.GetRegisterNameNew(instruction.Op1Register);
            var secondOp = context.GetConstantInReg(secondOpName);
            _interfaceType = (TypeDefinition) secondOp.Value;

            offsetReads = (InterfaceOffsetsReadAction) context.Actions.Last(a => a is InterfaceOffsetsReadAction);
            
            var cppType = SharedState.MonoToCppTypeDefs[_interfaceType];
            _matchingInterfaceOffset = offsetReads.InterfaceOffsets.First(i => i.type == cppType);
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
            return $"Checks for specific interface offset of type {_interfaceType.FullName} which resolves to offset {_matchingInterfaceOffset.offset}";
        }
    }
}