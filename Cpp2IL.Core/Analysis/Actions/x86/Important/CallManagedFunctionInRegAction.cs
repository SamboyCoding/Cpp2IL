using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class CallManagedFunctionInRegAction : BaseX86CallAction
    {
        public CallManagedFunctionInRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var regName = Utils.GetRegisterNameNew(instruction.MemoryBase);

            if (instruction.MemoryBase == Register.None)
                regName = Utils.GetRegisterNameNew(instruction.Op0Register);
            
            var operand = context.GetConstantInReg(regName);
            
            if(operand?.Value is MethodReference reference)
                ManagedMethodBeingCalled = reference;
            else if (operand?.Value is GenericMethodReference gmr)
            {
                ManagedMethodBeingCalled = gmr.Method;
                StaticMethodGenericTypeOverride = gmr.Type;
            }
            
            if(ManagedMethodBeingCalled == null)
                return;

            if (ManagedMethodBeingCalled.HasThis)
            {
                InstanceBeingCalledOn = context.GetLocalInReg("rcx");
                if (InstanceBeingCalledOn == null)
                {
                    var cons = context.GetConstantInReg("rcx");
                    if (cons?.Value is NewSafeCastResult<Instruction> castResult)
                        InstanceBeingCalledOn = castResult.original;
                }
            }

            if (!MethodUtils.CheckParameters(instruction, ManagedMethodBeingCalled, context, ManagedMethodBeingCalled.HasThis, out Arguments, failOnLeftoverArgs: false))
            {
                AddComment("Mismatched parameters detected here.");
            }
            
            CreateLocalForReturnType(context);
            
            RegisterLocals(context);
        }
    }
}