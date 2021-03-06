﻿using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LoadAttributeFromAttributeListAction : BaseAction
    {
        public LocalDefinition? LocalMade;
        private string? _destReg;
        public TypeDefinition? AttributeType;
        public long OffsetInList;

        public LoadAttributeFromAttributeListAction(MethodAnalysis context, Instruction instruction, List<TypeDefinition> attributes) : base(context, instruction)
        {
            var ptrSize = LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
            OffsetInList = instruction.MemoryDisplacement32 / ptrSize;

            AttributeType = attributes[(int) OffsetInList];

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            LocalMade = context.MakeLocal(AttributeType, reg: _destReg);
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
            return $"[!] Loads the attribute instance at offset {OffsetInList} which is of type {AttributeType}, and stores in new local {LocalMade} in {_destReg}";
        }
    }
}