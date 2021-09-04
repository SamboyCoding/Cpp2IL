using System;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    /// <summary>
    /// Action for a simple reg->reg move
    /// </summary>
    public class RegToRegMoveAction : BaseAction<Instruction>
    {
        private readonly IAnalysedOperand? beingMoved;
        private readonly string originalReg;
        private readonly string newReg;
        private LocalDefinition? _localBeingOverwritten;

        private readonly bool copyingValueNotLocal;

        public RegToRegMoveAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            originalReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            newReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            beingMoved = context.GetOperandInRegister(originalReg);
            _localBeingOverwritten = context.GetLocalInReg(newReg);

            context.SetRegContent(newReg, beingMoved);

            if (!(beingMoved is LocalDefinition localBeingMoved) || localBeingMoved.Type == null) return;

            if (localBeingMoved.Type.FullName == _localBeingOverwritten?.Type?.FullName && (localBeingMoved.KnownInitialValue == _localBeingOverwritten.KnownInitialValue))
            {
                //Variable being overwritten is the same type as this one - are we maybe just changing the value of that one (e.g. in a loop)?
                context.SetRegContent(newReg, _localBeingOverwritten);
                copyingValueNotLocal = true;
            }

            if (!context.IsIpInOneOrMoreLoops(instruction.IP)) return;
            
            var loopConditions = context.GetLoopConditionsInNestedOrder(instruction.IP);
            if (!(loopConditions.Last().GetArgumentAssociatedWithRegister(newReg) is { } argument) || !(argument is LocalDefinition argumentBeingOverwritten))
                return;

            if (localBeingMoved.Type != argumentBeingOverwritten.Type)
                return;

            //One of the arguments to our current loop condition is being overwritten
            _localBeingOverwritten = argumentBeingOverwritten;
            copyingValueNotLocal = true;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            //No-op
            if(!copyingValueNotLocal)
                return Array.Empty<Mono.Cecil.Cil.Instruction>();

            if (_localBeingOverwritten?.Variable == null)
                throw new TaintedInstructionException($"Local being overwritten, {_localBeingOverwritten} has been stripped or is a parameter");

            return new[]
            {
                context.GetILToLoad((LocalDefinition) beingMoved!, processor),
                processor.Create(OpCodes.Stloc, _localBeingOverwritten!.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            //If we're here, we know we're copying value, not just local, so we do a local substitute here:
            return $"{_localBeingOverwritten!.GetPseudocodeRepresentation()} = {beingMoved!.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"Copies {beingMoved} from {originalReg} into {newReg}";
        }

        public override bool IsImportant()
        {
            return copyingValueNotLocal && (_localBeingOverwritten != (LocalDefinition?) beingMoved);
        }
    }
}