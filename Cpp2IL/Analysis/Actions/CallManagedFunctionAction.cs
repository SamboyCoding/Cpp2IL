using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class CallManagedFunctionAction : BaseAction
    {
        private MethodDefinition? target;
        private List<IAnalysedOperand> arguments = new List<IAnalysedOperand>();
        
        public CallManagedFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var jumpTarget = LibCpp2ILUtils.GetJumpTarget(instruction, context.MethodStart + instruction.PC);
            var calledOn = context.GetLocalInReg("rcx");
            var listOfCallableMethods = LibCpp2IlMain.GetListOfMethodImplementationsAtAddress(jumpTarget);

            if (listOfCallableMethods == null) return;

            if (calledOn?.Type == null) return;
            
            //Direct instance methods take priority
            var possibleTarget = listOfCallableMethods.FirstOrDefault(m => !m.IsStatic && m.parameterCount > 0 && Utils.DoTypesMatch(m.Parameters![0].Type, calledOn.Type));
                
            //todo check args and null out

            if (possibleTarget != null)
            {
                target = SharedState.UnmanagedToManagedMethods[possibleTarget];
                return;
            }
            // SharedState.MethodsByAddress.TryGetValue(jumpTarget, out target);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Calls managed method {target?.FullName}";
        }
    }
}