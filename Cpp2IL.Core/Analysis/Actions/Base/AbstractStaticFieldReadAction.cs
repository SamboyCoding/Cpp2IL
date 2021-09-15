using System;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractStaticFieldReadAction<T> : BaseAction<T>
    {
        public FieldDefinition? FieldRead;
        public LocalDefinition? LocalWritten;

        protected AbstractStaticFieldReadAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (FieldRead == null || LocalWritten == null)
                throw new TaintedInstructionException("Couldn't identify the field being read (or its type).");

            if (LocalWritten.Variable == null)
                //Stripped out - no use found for the dest local.
                return Array.Empty<Instruction>();
            
            return new[]
            {
                processor.Create(OpCodes.Ldsfld, processor.ImportReference(FieldRead)),
                processor.Create(OpCodes.Stloc, LocalWritten.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            return $"{LocalWritten?.Type?.FullName} {LocalWritten?.Name} = {FieldRead?.DeclaringType?.FullName}.{FieldRead?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads the static field {FieldRead?.FullName} into new local {LocalWritten?.Name}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}