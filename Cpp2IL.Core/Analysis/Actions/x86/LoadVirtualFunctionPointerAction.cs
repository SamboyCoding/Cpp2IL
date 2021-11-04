using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class LoadVirtualFunctionPointerAction : BaseAction<Instruction>
    {
        private readonly string regReadFrom;
        private readonly Il2CppTypeDefinition? classReadFrom;
        private readonly MethodDefinition? methodPointerRead;
        private readonly ConstantDefinition? destinationConstant;
        private readonly int _slotNum;

        public LoadVirtualFunctionPointerAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            regReadFrom = Utils.Utils.GetRegisterNameNew(instruction.MemoryBase);
            var inReg = context.GetOperandInRegister(regReadFrom);

            if (inReg is not ConstantDefinition {Value: Il2CppClassIdentifier klass}) return;

            classReadFrom = klass.backingType;
            _slotNum = Utils.Utils.GetSlotNum((int) instruction.MemoryDisplacement32);
            
            methodPointerRead = MethodUtils.GetMethodFromVtableSlot(classReadFrom, _slotNum);

            if (methodPointerRead == null) return;

            var regPutInto = Utils.Utils.GetRegisterNameNew(instruction.Op0Register);
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
            return $"Loads the pointer to the implementation of virtual function {methodPointerRead?.FullName} specific to {classReadFrom?.FullName} from the class pointer in {regReadFrom}, slot {_slotNum} (from memory offset {AssociatedInstruction.MemoryDisplacement32}) and stores in constant {destinationConstant?.Name}";
        }
    }
}