using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractArrayAllocationAction<T> : BaseAction<T>
    {
        protected int SizeAllocated;
        protected TypeReference? TypeOfArray;
        protected LocalDefinition? LocalWritten;

        protected AbstractArrayAllocationAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (LocalWritten == null || TypeOfArray == null)
                throw new TaintedInstructionException("Missing created local or type of array");

            if (TypeOfArray is not ArrayType arrayType)
                throw new TaintedInstructionException("Array type isn't an array");

            return new []
            {
                processor.Create(OpCodes.Ldc_I4, SizeAllocated),
                processor.Create(OpCodes.Newarr, processor.ImportReference(arrayType.ElementType)),
                processor.Create(OpCodes.Stloc, LocalWritten.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            var aType = TypeOfArray as ArrayType;
            return $"{TypeOfArray?.FullName} {LocalWritten?.Name} = new {aType?.ElementType}[{SizeAllocated}]";
        }

        public override string ToTextSummary()
        {
            if (!(TypeOfArray is ArrayType))
                return $"[!!] Allocates an array of a type which isn't an array (got {TypeOfArray}), of size {SizeAllocated}, and stores the result as {LocalWritten?.Name}. This is a problem - we couldn't resolve the array type";
            
            return $"[!] Allocates an array of type {TypeOfArray?.FullName} of size {SizeAllocated} and stores the result as {LocalWritten?.Name} in register rax\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}