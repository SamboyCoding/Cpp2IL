using System;
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
        private FieldPointer? _boxedField;
        private ConstantDefinition ConstantDefinition;

        public UnboxObjectAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //TODO 32-bit, stack is different, need to pop instead of rdx, etc.
            localBeingUnboxed = context.GetLocalInReg("rcx");

            if (localBeingUnboxed == null)
                return;
            
            RegisterUsedLocal(localBeingUnboxed, context);
            
            ConstantDefinition = context.MakeConstant(typeof(LocalPointer), new LocalPointer(localBeingUnboxed), reg: "rax");
            
            //_localMade = context.MakeLocal(destinationType, reg: "rax");
        }
        public override bool IsImportant() => false;

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new NotImplementedException();
        }
        public override string? ToPsuedoCode()
        {
            throw new NotImplementedException();
            return $"{_localMade.Name} = {localBeingUnboxed.Name}";
        }

        public override string ToTextSummary()
        {
            return "";
        }
    }
}