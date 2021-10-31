using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64LoadAttributeFromAttributeListAction : AbstractAttributeLoadFromListAction<Arm64Instruction>
    {
        public Arm64LoadAttributeFromAttributeListAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, List<TypeDefinition> attributes) : base(context, instruction)
        {
            var ptrSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
            OffsetInList = instruction.MemoryOffset() / ptrSize;
            
            if(OffsetInList < 0 || OffsetInList >= attributes.Count)
                return;

            _attributeType = attributes[(int) OffsetInList];

            var destReg = Utils.Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            LocalMade = context.MakeLocal(_attributeType, reg: destReg);
        }
    }
}