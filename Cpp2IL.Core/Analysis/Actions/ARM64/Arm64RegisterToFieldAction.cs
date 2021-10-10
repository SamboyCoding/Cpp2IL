using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64RegisterToFieldAction : AbstractFieldWriteAction<Arm64Instruction>
    {
        private IAnalysedOperand? _sourceOperand;

        public Arm64RegisterToFieldAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id);
            InstanceBeingSetOn = context.GetLocalInReg(memReg);

            var sourceReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            _sourceOperand = context.GetOperandInRegister(sourceReg);
            
            if(InstanceBeingSetOn?.Type == null)
                return;
            
            RegisterUsedLocal(InstanceBeingSetOn, context);
            
            if(_sourceOperand is LocalDefinition l)
                RegisterUsedLocal(l, context);

            FieldWritten = FieldUtils.GetFieldBeingAccessed(InstanceBeingSetOn.Type, (ulong)instruction.MemoryOffset(), sourceReg[0] == 'v');
        }

        protected override string? GetValueSummary() => _sourceOperand?.ToString();

        protected override string? GetValuePseudocode() => _sourceOperand?.GetPseudocodeRepresentation();

        protected override Instruction[] GetIlToLoadValue(MethodAnalysis<Arm64Instruction> context, ILProcessor processor) => _sourceOperand?.GetILToLoad(context, processor) ?? Array.Empty<Instruction>();
    }
}