using System;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class LoadVirtualFunctionPointerAction : BaseAction
    {
        private string regReadFrom;
        private Il2CppTypeDefinition classReadFrom;
        private MethodDefinition? methodPointerRead;
        private ConstantDefinition? destinationConstant;

        public LoadVirtualFunctionPointerAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            regReadFrom = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var inReg = context.GetOperandInRegister(regReadFrom);

            if (!(inReg is ConstantDefinition cons) || !(cons.Value is Il2CppClassIdentifier klass)) return;

            classReadFrom = klass.backingType;

            var readOffset = instruction.MemoryDisplacement;
            methodPointerRead = Utils.GetMethodFromReadKlassOffset((int) readOffset);

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

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
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