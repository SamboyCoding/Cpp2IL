using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractArrayOffsetWriteAction<T> : BaseAction<T>
    {
        protected LocalDefinition? TheArray;
        
        protected AbstractArrayOffsetWriteAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }

        protected abstract int GetOffsetWritten();
        protected abstract string? GetPseudocodeValue();
        protected abstract string? GetSummaryValue();
        protected abstract Instruction[] GetInstructionsToLoadValue(MethodAnalysis<T> context, ILProcessor processor);

        public sealed override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (TheArray == null)
                throw new TaintedInstructionException("Array is null");

            if (TheArray.Type?.IsArray != true)
                throw new TaintedInstructionException("Array type is not an array");

            var valueLoad = GetInstructionsToLoadValue(context, processor);
            
            if (valueLoad.Length == 0)
                throw new TaintedInstructionException("Value to load is null");
            
            if (GetOffsetWritten() < 0)
                throw new TaintedInstructionException($"Index is < 0: {GetOffsetWritten()}");

            var ret = new List<Instruction>();
            
            //Load array
            ret.AddRange(TheArray.GetILToLoad(context, processor));
            
            //Load offset
            ret.Add(processor.Create(OpCodes.Ldc_I4, (int) GetOffsetWritten()));
            
            //Load value
            ret.AddRange(GetInstructionsToLoadValue(context, processor));
            
            //Store in array
            ret.Add(processor.Create(OpCodes.Stelem_Any, processor.ImportReference(TheArray.Type.GetElementType())));

            return ret.ToArray();
        }

        public sealed override string? ToPsuedoCode()
        {
            return $"{TheArray?.Name}[{GetOffsetWritten()}] = {GetPseudocodeValue()}";
        }

        public sealed override string ToTextSummary()
        {
            return $"Sets the value at offset {GetOffsetWritten()} in array {TheArray?.Name} to {GetSummaryValue()}";
        }

        public sealed override bool IsImportant()
        {
            return true;
        }
    }
}