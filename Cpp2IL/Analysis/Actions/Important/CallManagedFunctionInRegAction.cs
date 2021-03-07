using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallManagedFunctionInRegAction : AbstractCallAction
    {
        public CallManagedFunctionInRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var regName = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var operand = context.GetConstantInReg(regName);
            ManagedMethodBeingCalled = (MethodDefinition) operand?.Value;
            
            if(ManagedMethodBeingCalled == null)
                return;

            if (ManagedMethodBeingCalled.HasThis)
            {
                InstanceBeingCalledOn = context.GetLocalInReg("rcx");
                if (InstanceBeingCalledOn == null)
                {
                    var cons = context.GetConstantInReg("rcx");
                    if (cons?.Value is NewSafeCastResult castResult)
                        InstanceBeingCalledOn = castResult.original;
                }
            }

            if (!MethodUtils.CheckParameters(instruction, ManagedMethodBeingCalled, context, ManagedMethodBeingCalled.HasThis, out Arguments, failOnLeftoverArgs: false))
            {
                AddComment("Mismatched parameters detected here.");
            }
            
            CreateLocalForReturnType(context);
            
            RegisterLocals();
        }
    }
}