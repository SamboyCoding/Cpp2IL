using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class CallVirtualMethodAction : BaseX86CallAction
    {
        public CallVirtualMethodAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            ShouldUseCallvirt = true;
            
            var inReg = context.GetOperandInRegister(X86Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (!(inReg is ConstantDefinition {Value: Il2CppClassIdentifier klass})) return;
            
            var classReadFrom = klass.backingType;
            var slotNum = MiscUtils.GetSlotNum((int) instruction.MemoryDisplacement);
            
            ManagedMethodBeingCalled = MethodUtils.GetMethodFromVtableSlot(classReadFrom, slotNum);

            if (ManagedMethodBeingCalled == null) return;

            InstanceBeingCalledOn = ManagedMethodBeingCalled.HasThis ? context.GetLocalInReg("rcx") : null;

            if(!MethodUtils.CheckParameters(instruction, ManagedMethodBeingCalled, context, ManagedMethodBeingCalled.HasThis, out Arguments, InstanceBeingCalledOn?.Type, false))
                AddComment("Arguments are incorrect?");

            CreateLocalForReturnType(context);
            RegisterLocals(context);
        }
    }
}