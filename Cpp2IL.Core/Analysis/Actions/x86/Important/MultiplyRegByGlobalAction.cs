using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class MultiplyRegByGlobalAction : BaseAction<Instruction>
    {
        private LocalDefinition? _op1;
        private string? _regName;
        private float _globalValue;
        private LocalDefinition? _localMade;
        private ulong _globalAddr;

        public MultiplyRegByGlobalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _globalAddr = instruction.MemoryDisplacement64;
            
            _regName = X86Utils.GetRegisterNameNew(instruction.Op0Register);
            _op1 = context.GetLocalInReg(_regName);

   
            if(_op1 is {})
                RegisterUsedLocal(_op1, context);

            // TODO: Extend for doubles?
            _globalValue = BitConverter.ToSingle(LibCpp2IlMain.Binary!.GetRawBinaryContent(), (int) LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(_globalAddr));

            _localMade = context.MakeLocal(TypeDefinitions.Single, reg: _regName);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_op1 is null || _localMade?.Variable is null)
                throw new TaintedInstructionException("Operand we were adding to is null or local made was stripped");

            List<Mono.Cecil.Cil.Instruction> instructions = new();
            
            instructions.AddRange(_op1.GetILToLoad(context, processor));
            
            instructions.Add(processor.Create(OpCodes.Ldc_R4, _globalValue)); 
            
            instructions.Add(processor.Create(OpCodes.Mul));
            
            instructions.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));

            return instructions.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type} {_localMade?.Name} = {_op1?.GetPseudocodeRepresentation()} * {_globalValue}";
        }

        public override string ToTextSummary()
        {
            return $"Multiplies {_op1} by the constant value at 0x{_globalAddr:X} in the binary, which is {_globalValue}, and stores the result in new local {_localMade} in register {_regName}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}