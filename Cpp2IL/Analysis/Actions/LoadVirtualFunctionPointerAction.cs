using System;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using SharpDisasm;

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
            regReadFrom = Utils.GetRegisterName(instruction.Operands[1]);
            var inReg = context.GetOperandInRegister(regReadFrom);

            if (!(inReg is ConstantDefinition cons) || !(cons.Value is Il2CppClassIdentifier klass)) return;

            classReadFrom = klass.backingType;
            
            var readOffset = Utils.GetOperandMemoryOffset(instruction.Operands[1]);
            methodPointerRead = Utils.GetMethodFromReadKlassOffset(readOffset);

            if (methodPointerRead == null) return;

            var regPutInto = Utils.GetRegisterName(instruction.Operands[0]);
            if (regPutInto == "rsp")
            {
                var stackOffset = Utils.GetOperandMemoryOffset(instruction.Operands[0]);
                context.PushToStack(context.MakeConstant(typeof(MethodDefinition), methodPointerRead), stackOffset);
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