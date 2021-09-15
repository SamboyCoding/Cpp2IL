using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractArrayOffsetReadAction<T> : BaseAction<T>
    {
        protected LocalDefinition? ArrayLocal;
        protected LocalDefinition? OffsetLocal;
        protected ArrayType? ArrType;
        public LocalDefinition? LocalMade;
        protected TypeReference? ArrayElementType;

        protected AbstractArrayOffsetReadAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (LocalMade == null || ArrayLocal == null || OffsetLocal == null)
                throw new TaintedInstructionException("Array, offset, or destination is null");

            if (ArrayElementType == null)
                throw new TaintedInstructionException("Type of elements in the array is null");

            if (LocalMade.Variable == null)
                //Stripped out - couldn't find a usage for this local.
                return Array.Empty<Instruction>();

            var ret = new List<Instruction>();
            
            //Load array
            ret.AddRange(ArrayLocal.GetILToLoad(context, processor));
            
            //Load index
            ret.AddRange(OffsetLocal.GetILToLoad(context, processor));

            //Pop offset and array, push element
            ret.Add(processor.Create(OpCodes.Ldelem_Any, processor.ImportReference(ArrayElementType!)));
            
            //Store item in local
            ret.Add(processor.Create(OpCodes.Stloc, LocalMade.Variable));

            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{ArrType?.ElementType} {LocalMade?.GetPseudocodeRepresentation()} = {ArrayLocal?.GetPseudocodeRepresentation()}[{OffsetLocal?.GetPseudocodeRepresentation()}]";
        }

        public override string ToTextSummary()
        {
            return $"Copies the element in the array {ArrayLocal} at the index specified by {OffsetLocal} into new local {LocalMade}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}