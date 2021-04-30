using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LoadAttributeFromAttributeListAction : BaseAction
    {
        private LocalDefinition? _localMade;
        private string? _destReg;
        private TypeDefinition? _attributeType;
        private long _offsetInList;

        public LoadAttributeFromAttributeListAction(MethodAnalysis context, Instruction instruction, List<MethodReference> ctors) : base(context, instruction)
        {
            var ptrSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
            _offsetInList = instruction.MemoryDisplacement32 / ptrSize;

            var ctor = ctors[(int) _offsetInList];

            _attributeType = ctor.DeclaringType.Resolve();

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            _localMade = context.MakeLocal(_attributeType, reg: _destReg);
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
            return $"[!] Loads the attribute instance at offset {_offsetInList} which is of type {_attributeType}, and stores in new local {_localMade} in {_destReg}";
        }
    }
}