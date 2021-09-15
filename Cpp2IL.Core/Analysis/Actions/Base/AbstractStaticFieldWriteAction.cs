using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractStaticFieldWriteAction<T> : BaseAction<T>
    {
        protected IAnalysedOperand? _sourceOperand;
        protected FieldDefinition? _theField;

        protected AbstractStaticFieldWriteAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (_theField == null || _sourceOperand == null)
                throw new TaintedInstructionException();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            if(_sourceOperand is ConstantDefinition c)
                ret.AddRange(c.GetILToLoad(context, processor));
            else
                ret.Add(context.GetIlToLoad((LocalDefinition) _sourceOperand, processor));
            
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