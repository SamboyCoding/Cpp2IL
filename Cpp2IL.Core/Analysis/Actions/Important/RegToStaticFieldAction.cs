using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class RegToStaticFieldAction : BaseAction
    {
        private IAnalysedOperand? _sourceOperand;
        private FieldDefinition? _theField;

        public RegToStaticFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _sourceOperand = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.Op1Register));
            var destStaticFieldsPtr = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            var staticFieldOffset = instruction.MemoryDisplacement;

            if (!(destStaticFieldsPtr?.Value is StaticFieldsPtr staticFieldsPtr)) 
                return;

            if (_sourceOperand is LocalDefinition l)
                RegisterUsedLocal(l);

            _theField = FieldUtils.GetStaticFieldByOffset(staticFieldsPtr, staticFieldOffset);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (_theField == null || _sourceOperand == null)
                throw new TaintedInstructionException();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            if(_sourceOperand is ConstantDefinition c)
                ret.AddRange(c.GetILToLoad(context, processor));
            else
                ret.Add(context.GetILToLoad((LocalDefinition) _sourceOperand, processor));
            
            ret.Add(processor.Create(OpCodes.Stsfld, processor.ImportReference(_theField)));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_theField?.DeclaringType.FullName}.{_theField?.Name} = {_sourceOperand?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets static field {_theField?.DeclaringType.FullName}.{_theField?.Name} to {_sourceOperand}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}