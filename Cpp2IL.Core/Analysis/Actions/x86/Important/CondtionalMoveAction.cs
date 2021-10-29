using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ConditionalMoveAction : BaseAction<Instruction>
    {
        protected readonly ComparisonAction? _associatedCompare;
        protected readonly BaseAction<Instruction> _moveAction;
        private bool nullMode = false;
        private bool booleanMode = false;
        public ConditionalMoveAction(MethodAnalysis<Instruction> context, Instruction instruction, BaseAction<Instruction> moveAction) : base(context, instruction)
        {
            _associatedCompare = (ComparisonAction?) context.Actions.LastOrDefault(a => a is ComparisonAction);
            _moveAction = moveAction;

            if (_associatedCompare != null && (instruction.Mnemonic == Mnemonic.Cmove || instruction.Mnemonic == Mnemonic.Cmovne))
            {
                nullMode = _associatedCompare.ArgumentOne == _associatedCompare.ArgumentTwo;
                booleanMode = nullMode && _associatedCompare.ArgumentOne is LocalDefinition local && local.Type?.FullName == "System.Boolean";
            }
        }

        public bool IsTypeCheckCondtionalMove()
        {
            if (_associatedCompare?.ArgumentTwo is null)
                return false;

            if (_associatedCompare.ArgumentOne is ConstantDefinition {Value: Il2CppClassIdentifier} && _associatedCompare.ArgumentTwo is ConstantDefinition {Value: TypeDefinition or TypeReference} && AssociatedInstruction.Mnemonic == Mnemonic.Cmove)
            {
                return true;
            }

            return false;
        }

        public override bool IsImportant() => !IsTypeCheckCondtionalMove() && _moveAction.IsImportant();

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_associatedCompare?.ArgumentOne == null || (!OnlyNeedToLoadOneOperand() && _associatedCompare.ArgumentTwo == null))
                throw new TaintedInstructionException("One of the arguments is null");

            var ret = new List<Mono.Cecil.Cil.Instruction>();       
            var target = processor.Create(OpCodes.Nop);

            ret.AddRange(_associatedCompare.ArgumentOne.GetILToLoad(context, processor));

            if (!OnlyNeedToLoadOneOperand())
                ret.AddRange(_associatedCompare.ArgumentTwo.GetILToLoad(context, processor));
            
            ret.Add(processor.Create(GetJumpOpcode(), target));

            ret.AddRange(_moveAction.ToILInstructions(context, processor));

            ret.Add(target);

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"if ({GetArgumentOnePseudocodeValue()} {GetJumpOpCodePseudoCodeValue()} {GetArgumentTwoPseudocodeValue()}) {_moveAction.ToPsuedoCode()}";
        }
        
        protected string GetArgumentOnePseudocodeValue()
        {
            return _associatedCompare?.ArgumentOne == null ? "" : _associatedCompare.ArgumentOne.GetPseudocodeRepresentation();
        }

        protected string GetArgumentTwoPseudocodeValue()
        {
            return _associatedCompare?.ArgumentTwo == null ? "" : _associatedCompare.ArgumentTwo.GetPseudocodeRepresentation();
        }

        public override string ToTextSummary()
        {
            return $"[!] {_moveAction.ToTextSummary()} based on previous comparison";
        }

        protected string GetJumpOpCodePseudoCodeValue()
        {
            switch (AssociatedInstruction.Mnemonic)
            {
                case Mnemonic.Cmove:
                    return "==";
                case Mnemonic.Cmovne:
                    return "!=";
                case Mnemonic.Cmova:
                case Mnemonic.Cmovg:
                    return ">";
                case Mnemonic.Cmovae:
                case Mnemonic.Cmovge:
                    return ">=";
                case Mnemonic.Cmovb:
                case Mnemonic.Cmovl:
                    return "<";
                case Mnemonic.Cmovbe:
                case Mnemonic.Cmovle:
                    return "<=";
                default:
                    return "(Unknown conditional operation)";
            }
        }
        
        protected OpCode GetJumpOpcode()
        {
            switch (AssociatedInstruction.Mnemonic)
            {
                case Mnemonic.Cmove:
                    return OnlyNeedToLoadOneOperand() ? OpCodes.Brtrue : OpCodes.Bne_Un;
                case Mnemonic.Cmovne:
                    return OnlyNeedToLoadOneOperand() ? OpCodes.Brfalse : OpCodes.Beq;
                case Mnemonic.Cmovg:
                    return OpCodes.Ble;
                case Mnemonic.Cmovge:
                    return OpCodes.Blt;
                case Mnemonic.Cmovl:
                    return OpCodes.Bge;
                case Mnemonic.Cmovle:
                    return OpCodes.Bgt;
                case Mnemonic.Cmova:
                    return OpCodes.Ble_Un;
                case Mnemonic.Cmovae:
                    return OpCodes.Blt_Un;
                case Mnemonic.Cmovb:
                    return OpCodes.Bge_Un;
                case Mnemonic.Cmovbe:
                    return OpCodes.Bgt_Un;
                default:
                    throw new NotImplementedException($"Il generation for {AssociatedInstruction.Mnemonic} isn't implemented");
                // TODO: Other Conditional Move Instructions?
                // Not sure if they actually show up anywhere
                //case Mnemonic.Cmovs: 
                //case Mnemonic.Cmovns:
            }
        }
        protected bool OnlyNeedToLoadOneOperand() => booleanMode || nullMode;
    }
}