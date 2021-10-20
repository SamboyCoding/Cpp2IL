using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractAttributeLoadFromListAction<T> : BaseAction<T>
    {
        public LocalDefinition? LocalMade;
        public long OffsetInList;
        protected TypeDefinition? _attributeType;
        
        protected AbstractAttributeLoadFromListAction(MethodAnalysis<T> context, T instruction) : base(context, instruction) { }

        public sealed override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor) => throw new System.InvalidOperationException("Should not be attempting to generate IL for this type of instruction!");

        public sealed override string? ToPsuedoCode() => throw new System.InvalidOperationException("Should not be attempting to generate pseudocode for this type of instruction!");

        public sealed override string ToTextSummary() => $"[!] Loads the attribute instance at offset {OffsetInList} which is of type {_attributeType}, and stores in new local {LocalMade}";

        public sealed override bool IsImportant() => false;

        public sealed override bool PseudocodeNeedsLinebreakBefore() => false;
    }
}