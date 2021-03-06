﻿using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.Metadata;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class InterfaceOffsetsReadAction : BaseAction
    {
        internal Il2CppInterfaceOffset[] InterfaceOffsets;
        public Il2CppClassIdentifier loadedFor;
        private ConstantDefinition _destinationConst;
        private string _destReg;

        public InterfaceOffsetsReadAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var regName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var regConstant = context.GetConstantInReg(regName);

            loadedFor = (Il2CppClassIdentifier) regConstant.Value;
            InterfaceOffsets = loadedFor.backingType.InterfaceOffsets;

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            _destinationConst = context.MakeConstant(typeof(Il2CppInterfaceOffset[]), InterfaceOffsets, reg: _destReg);
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
            return $"Loads the interface offsets for the class pointer to {loadedFor.backingType.FullName}, which contains {InterfaceOffsets.Length} offsets, and stores them as a constant {_destinationConst.Name} in reg {_destReg}";
        }
    }
}