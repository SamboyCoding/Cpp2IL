﻿using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class ConstantArrayOffsetPointerToRegAction : BaseAction<Instruction>
    {
        private readonly LocalDefinition? _arrayLocal;
        private readonly int _index;
        private readonly ConstantDefinition? _destConstant;
        private TypeReference? _elementType;
        private string? _destinationReg;

        public ConstantArrayOffsetPointerToRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //God knows why the memory *index* contains the array, and the base contains the index, but it does.
            var arrayContainingReg = X86Utils.GetRegisterNameNew(instruction.MemoryBase);
            _destinationReg = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            var arrayOffset = instruction.MemoryDisplacement;

            _arrayLocal = context.GetLocalInReg(arrayContainingReg);

            if (_arrayLocal?.Type?.IsArray != true) return;
            
            RegisterUsedLocal(_arrayLocal, context);

            _index = (int) ((arrayOffset - Il2CppArrayUtils.FirstItemOffset) / MiscUtils.GetPointerSizeBytes());
            
            //Regardless of if we have an index local, we can still work out the type of the array and make a local.
            //Resolve() turns array types into non-array types

            _elementType = _arrayLocal.Type is ArrayType at ? at.ElementType : _arrayLocal.Type.Resolve();

            _destConstant = context.MakeConstant(typeof(Il2CppArrayOffsetPointer<Instruction>), new Il2CppArrayOffsetPointer<Instruction>(_arrayLocal, _index), reg: _destinationReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Reads the pointer to the value in the array {_arrayLocal} at index {_index}, into a new constant {_destConstant} in {_destinationReg}";
        }
    }
}