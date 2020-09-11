using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class CallManagedFunctionAction : BaseAction
    {
        private MethodDefinition? target;
        private List<IAnalysedOperand> arguments = new List<IAnalysedOperand>();

        private bool CheckParameters(Il2CppMethodDefinition method, MethodAnalysis context, bool isInstance)
        {
            var actualArgs = new List<IAnalysedOperand>();
            if(!isInstance)
                actualArgs.Add(context.GetOperandInRegister("rcx") ?? context.GetOperandInRegister("xmm0"));
            
            actualArgs.Add(context.GetOperandInRegister("rdx") ?? context.GetOperandInRegister("xmm1"));
            actualArgs.Add(context.GetOperandInRegister("r8") ?? context.GetOperandInRegister("xmm2"));
            actualArgs.Add(context.GetOperandInRegister("r9") ?? context.GetOperandInRegister("xmm3"));
            
            foreach (var parameterData in method.Parameters!)
            {
                if (actualArgs.Count(a => a != null) == 0) return false;

                var arg = actualArgs.RemoveAndReturn(0);
                switch (arg)
                {
                    case ConstantDefinition cons when cons.Type.FullName != parameterData.Type.ToString(): //Constant type mismatch
                    case LocalDefinition local when !Utils.IsManagedTypeAnInstanceOfCppOne(parameterData.Type, local.Type!): //Local type mismatch
                        return false;
                }
            }

            if (actualArgs.Any(a => a != null))
                return false; //Left over args - it's probably not this one

            return true;
        }

        public CallManagedFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var jumpTarget = Utils.GetJumpTarget(instruction, context.MethodStart + instruction.PC);
            var objectMethodBeingCalledOn = context.GetLocalInReg("rcx");
            var listOfCallableMethods = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(jumpTarget);

            if (listOfCallableMethods == null) return;

            if (objectMethodBeingCalledOn?.Type == null) return;

            //Direct instance methods take priority
            var possibleTarget = listOfCallableMethods.FirstOrDefault(m => !m.IsStatic && Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(m.DeclaringType!), objectMethodBeingCalledOn.Type) && CheckParameters(m, context, true));

            //todo check args and null out

            if (possibleTarget == null)
                //Base class instance methods
                possibleTarget = listOfCallableMethods.FirstOrDefault(m => !m.IsStatic && Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(m.DeclaringType!), objectMethodBeingCalledOn.Type) && CheckParameters(m, context, true));
            
            //check args again.

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
            return $"Calls managed method {target?.FullName}\n";
        }
    }
}