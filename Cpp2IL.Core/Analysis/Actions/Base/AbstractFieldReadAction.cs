using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractFieldReadAction<T> : BaseAction<T>
    {
        public FieldUtils.FieldBeingAccessedData? FieldRead;
        public LocalDefinition? LocalWritten;
        protected LocalDefinition? ReadFrom;
        
        protected AbstractFieldReadAction(MethodAnalysis<T> context, T instruction) : base(context, instruction)
        {
        }
        
        public override string ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {ReadFrom?.Name}.{FieldRead}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {FieldRead} from {ReadFrom} and stores in a new local {LocalWritten}\n";
        }
        
        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (LocalWritten == null || ReadFrom == null || FieldRead == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Instruction>();

            //Load object
            ret.AddRange(ReadFrom.GetILToLoad(context, processor));

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