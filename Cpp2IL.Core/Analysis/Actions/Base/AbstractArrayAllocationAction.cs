using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractArrayAllocationAction<T> : BaseAction<T>
    {
        protected int SizeAllocated;
        protected bool LocalArraySize;
        protected LocalDefinition? LocalUsedForArraySize;
        protected TypeReference? TypeOfArray;
        protected LocalDefinition? LocalWritten;

        protected AbstractArrayAllocationAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (LocalWritten == null || TypeOfArray == null)
                throw new TaintedInstructionException("Missing created local or type of array");

            if (TypeOfArray is not ArrayType arrayType)
                throw new TaintedInstructionException("Array type isn't an array");

            if (!LocalArraySize)
            {
                return new[]
                {
                    processor.Create(OpCodes.Ldc_I4, SizeAllocated),
                    processor.Create(OpCodes.Newarr, processor.ImportReference(arrayType.ElementType)),
                    processor.Create(OpCodes.Stloc, LocalWritten.Variable)
                };
            }
            else
            {
                if(LocalUsedForArraySize == null)
                    throw new TaintedInstructionException("Missing local used for array size");
                List<Instruction> instructions = new List<Instruction>();
                instructions.AddRange(LocalUsedForArraySize.GetILToLoad(context, processor));
                instructions.Add(processor.Create(OpCodes.Newarr, processor.ImportReference(arrayType.ElementType)));
                instructions.Add(processor.Create(OpCodes.Stloc, LocalWritten.Variable));
                return instructions.ToArray();
            }
        }

        public override string? ToPsuedoCode()
        {
            var aType = TypeOfArray as ArrayType;
            return $"{TypeOfArray?.FullName} {LocalWritten?.Name} = new {aType?.ElementType}[{(LocalArraySize ? LocalUsedForArraySize?.Name : SizeAllocated)}]";
        }

        public override string ToTextSummary()
        {
            if (!(TypeOfArray is ArrayType))
                return $"[!!] Allocates an array of a type which isn't an array (got {TypeOfArray}), of size {(LocalArraySize ? LocalUsedForArraySize?.Name : SizeAllocated)}, and stores the result as {LocalWritten?.Name}. This is a problem - we couldn't resolve the array type";
            
            return $"[!] Allocates an array of type {TypeOfArray?.FullName} of size {(LocalArraySize ? LocalUsedForArraySize?.Name : SizeAllocated)} and stores the result as {LocalWritten?.Name} in register rax\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}