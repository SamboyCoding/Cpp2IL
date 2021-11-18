using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class LoadAttributeFromAttributeListAction : AbstractAttributeLoadFromListAction<Instruction>
    {
        public LoadAttributeFromAttributeListAction(MethodAnalysis<Instruction> context, Instruction instruction, List<TypeDefinition> attributes) : base(context, instruction)
        {
            var ptrSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
            OffsetInList = instruction.MemoryDisplacement32 / ptrSize;

            _attributeType = attributes[(int) OffsetInList];

            var destReg = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            LocalMade = context.MakeLocal(_attributeType, reg: destReg);
        }
    }
}