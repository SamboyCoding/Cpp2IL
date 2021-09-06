using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractFieldReadAction<T> : BaseAction<T>
    {
        public FieldUtils.FieldBeingAccessedData? FieldRead;
        public LocalDefinition<T>? LocalWritten;
        protected LocalDefinition<T>? _readFrom;
        
        protected AbstractFieldReadAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }
        
        public override string ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {_readFrom?.Name}.{FieldRead}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {FieldRead} from {_readFrom} and stores in a new local {LocalWritten}\n";
        }
        
        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (LocalWritten == null || _readFrom == null || FieldRead == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            //Load object
            ret.AddRange(_readFrom.GetILToLoad(context, processor));

            //Access field
            ret.AddRange(FieldRead.GetILToLoad(processor));

            //Store to local
            ret.Add(processor.Create(OpCodes.Stloc, LocalWritten.Variable));
            
            return ret.ToArray();
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}