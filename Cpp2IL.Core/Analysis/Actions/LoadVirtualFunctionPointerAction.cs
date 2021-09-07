using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class LoadVirtualFunctionPointerAction : BaseAction<Instruction>
    {
        private string regReadFrom;
        private Il2CppTypeDefinition classReadFrom;
        private MethodDefinition? methodPointerRead;
        private ConstantDefinition? destinationConstant;

        public LoadVirtualFunctionPointerAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            regReadFrom = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var inReg = context.GetOperandInRegister(regReadFrom);

            if (!(inReg is ConstantDefinition {Value: Il2CppClassIdentifier klass})) return;

            classReadFrom = klass.backingType;
            var slotNum = Utils.GetSlotNum((int) instruction.MemoryDisplacement);
            
            methodPointerRead = MethodUtils.GetMethodFromVtableSlot(classReadFrom, slotNum);

            if (methodPointerRead == null) return;

            var regPutInto = Utils.GetRegisterNameNew(instruction.Op0Register);
            if (regPutInto == "rsp")
            {
                //todo how do we handle this kind of instruction - does it even exist?
                // var stackOffset = Utils.GetOperandMemoryOffset(instruction.Operands[0]);
                // context.PushToStack(context.MakeConstant(typeof(MethodDefinition), methodPointerRead), stackOffset);
            }
            else
            {
                destinationConstant = context.MakeConstant(typeof(MethodDefinition), methodPointerRead, reg: regPutInto);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the pointer to the implementation of virtual function {methodPointerRead?.FullName} specific to {classReadFrom?.FullName} from the class pointer in {regReadFrom} and stores in constant {destinationConstant?.Name}";
        }
    }
}