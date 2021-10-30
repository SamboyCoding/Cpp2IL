using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ConditionalGlobalStringRefToConstantAction : ConditionalMoveAction
    {
        private GlobalStringRefToConstantAction? AssociatedStringLoad;
        private LocalDefinition? LocalCreated;
        private string? _destReg;

        public ConditionalGlobalStringRefToConstantAction(MethodAnalysis<Instruction> context, Instruction instruction, BaseAction<Instruction> baseAction) : base(context, instruction, baseAction)
        {
            if(_associatedCompare == null)
                return;
            
            var expectedIndex = context.Actions.IndexOf(_associatedCompare) + 1;
            if (expectedIndex < context.Actions.Count && context.Actions[expectedIndex] is GlobalStringRefToConstantAction globalStringRefToConstantAction)
            {
                AssociatedStringLoad = globalStringRefToConstantAction;
            }
            else if (context.Actions[context.Actions.IndexOf(_associatedCompare) - 1] is GlobalStringRefToConstantAction globalStringRefToConstantAction2)
            {
                AssociatedStringLoad = globalStringRefToConstantAction2;
            }
            else
            {
                // TODO: Account for other scenarios where its overwriting an old string thats not found?
                AddComment("Could not find associated string load. Bailing out.");
                return;
            }

            context.Actions.Remove(AssociatedStringLoad);

            _destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;

            var whatWeWant = context.DeclaringType.Module.ImportReference(Utils.StringReference);

            var localAtDest = AssociatedStringLoad.LastKnownLocalInReg;

            if ("System.String".Equals(localAtDest?.Type?.FullName))
                LocalCreated = localAtDest;
            else
                LocalCreated = context.MakeLocal(whatWeWant, null, _destReg, AssociatedStringLoad.ResolvedString);

            RegisterUsedLocal(LocalCreated, context);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_associatedCompare?.ArgumentOne == null || (!OnlyNeedToLoadOneOperand() && _associatedCompare.ArgumentTwo == null))
                throw new TaintedInstructionException("One of the arguments is null");

            if (LocalCreated == null)
                throw new TaintedInstructionException("Local created was null");

            if (AssociatedStringLoad == null)
                throw new TaintedInstructionException("Associated string load was null");

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            var target = processor.Create(OpCodes.Nop);


            ret.Add(processor.Create(OpCodes.Ldstr, AssociatedStringLoad.ResolvedString));
            ret.Add(processor.Create(OpCodes.Stloc, LocalCreated.Variable));

            ret.AddRange(_associatedCompare.ArgumentOne.GetILToLoad(context, processor));

            if (!OnlyNeedToLoadOneOperand())
                ret.AddRange(_associatedCompare.ArgumentTwo!.GetILToLoad(context, processor));

            ret.Add(processor.Create(GetJumpOpcode(), target));

            ret.Add(processor.Create(OpCodes.Ldstr, (_moveAction as GlobalStringRefToConstantAction)!.ResolvedString));
            ret.Add(processor.Create(OpCodes.Stloc, LocalCreated.Variable));

            ret.Add(target);

            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"string {LocalCreated?.Name} = {GetArgumentOnePseudocodeValue()} {GetJumpOpCodePseudoCodeValue()} {GetArgumentTwoPseudocodeValue()} ? \"{(_moveAction as GlobalStringRefToConstantAction)?.ResolvedString}\" : \"{AssociatedStringLoad?.ResolvedString}\"";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets local {LocalCreated?.Name} in {_destReg} to \"{(_moveAction as GlobalStringRefToConstantAction)!.ResolvedString}\" if {GetArgumentOnePseudocodeValue()} {GetJumpOpCodePseudoCodeValue()} {GetArgumentTwoPseudocodeValue()} else it sets the local to \"{AssociatedStringLoad?.ResolvedString ?? "[Unknown (ANALYSIS ERROR)]"}\"";
        }
    }
}