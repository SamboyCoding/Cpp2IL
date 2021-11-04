using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class UnboxObjectAction : BaseAction<Instruction>
    {
        private TypeReference? destinationType;
        private LocalDefinition? localBeingUnboxed;
        private LocalDefinition? _localMade;
        private bool _boxingFieldPointer = false;
        private FieldPointer _boxedField;

        public UnboxObjectAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //TODO 32-bit, stack is different, need to pop instead of rdx, etc.
            localBeingUnboxed = context.GetLocalInReg("rcx");

            if (localBeingUnboxed == null)
                return;
            
            RegisterUsedLocal(localBeingUnboxed, context);
            
            _localMade = context.MakeLocal(destinationType, reg: "rax");
        }
        public override bool IsImportant() => true;

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (localBeingUnboxed == null || _localMade == null)
                throw new TaintedInstructionException("Local being unboxed or local created was null");
            
            return new[]
            {
                context.GetIlToLoad(localBeingUnboxed!, processor),
                processor.Create(OpCodes.Stloc, _localMade.Variable)
            };
        }
        public override string? ToPsuedoCode()
        {
            return $"{_localMade.Name} = {localBeingUnboxed.Name}";
        }

        public override string ToTextSummary()
        {
            return $"Unboxes local {localBeingUnboxed.Name} to {_localMade.Name}";
        }
    }
}