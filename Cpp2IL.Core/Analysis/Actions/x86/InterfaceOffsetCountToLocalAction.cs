﻿using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class InterfaceOffsetCountToLocalAction : BaseAction<Instruction>
    {
        private ushort offsetCount;
        private LocalDefinition? _localMade;
        private Il2CppTypeDefinition? _typeCountReadFrom;

        public InterfaceOffsetCountToLocalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var constantBeingRead = context.GetConstantInReg(MiscUtils.GetRegisterNameNew(instruction.MemoryBase));

            if (constantBeingRead?.Type != typeof(Il2CppClassIdentifier))
                return;

            _typeCountReadFrom = (constantBeingRead.Value as Il2CppClassIdentifier)?.backingType;

            if (_typeCountReadFrom == null)
                return;

            offsetCount = _typeCountReadFrom?.interface_offsets_count ?? 0;

            if (offsetCount != 0)
                _localMade = context.MakeLocal(MiscUtils.UInt32Reference, reg: MiscUtils.GetRegisterNameNew(instruction.Op0Register), knownInitialValue: offsetCount);
            else
                _localMade = context.MakeLocal(MiscUtils.UInt32Reference, reg: MiscUtils.GetRegisterNameNew(instruction.Op0Register));
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
            return $"Reads the number of interface offsets for type {_typeCountReadFrom} (which is {offsetCount}) and stores in new local {_localMade}";
        }
    }
}