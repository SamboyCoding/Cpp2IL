using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    public class ComparisonAction : BaseAction
    {
        public IAnalysedOperand? ArgumentOne;
        public IAnalysedOperand? ArgumentTwo;

        public ComparisonAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var r0 = Utils.GetRegisterNameNew(instruction.Op0Register);
            var r1 = Utils.GetRegisterNameNew(instruction.Op1Register);
            var globalMemoryOffset = LibCpp2IlMain.ThePe.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();

            if (r0 != "rsp")
                if (instruction.Op0Kind == OpKind.Register)
                    ArgumentOne = context.GetOperandInRegister(r0);
                else if (instruction.Op0Kind.IsImmediate())
                    ArgumentOne = context.MakeConstant(typeof(int), instruction.GetImmediate(0));
                else if (instruction.Op0Kind == OpKind.Memory && instruction.MemoryBase != Register.None)
                    ArgumentOne = context.MakeConstant(typeof(string), $"{{A field on {context.GetLocalInReg(Utils.GetRegisterNameNew(instruction.MemoryBase))}, offset 0x{instruction.MemoryDisplacement:X}}}");
                else if (LibCpp2IlMain.GetAnyGlobalByAddress(globalMemoryOffset).Offset == globalMemoryOffset)
                    ArgumentOne = context.MakeConstant(typeof(GlobalIdentifier), LibCpp2IlMain.GetAnyGlobalByAddress(globalMemoryOffset));
                else
                    ArgumentOne = context.MakeConstant(typeof(UnknownGlobalAddr), new UnknownGlobalAddr(globalMemoryOffset));
            if (r1 != "rsp")
                if (instruction.Op1Kind == OpKind.Register)
                    ArgumentTwo = context.GetOperandInRegister(r1);
                else if (instruction.Op1Kind.IsImmediate())
                    ArgumentTwo = context.MakeConstant(typeof(int), instruction.GetImmediate(1));
                else if (instruction.Op1Kind == OpKind.Memory && instruction.MemoryBase != Register.None)
                    ArgumentOne = context.MakeConstant(typeof(string), $"{{A field on {context.GetLocalInReg(Utils.GetRegisterNameNew(instruction.MemoryBase))}, offset 0x{instruction.MemoryDisplacement:X}}}");
                else if (LibCpp2IlMain.GetAnyGlobalByAddress(globalMemoryOffset).Offset == globalMemoryOffset)
                    ArgumentTwo = context.MakeConstant(typeof(GlobalIdentifier), LibCpp2IlMain.GetAnyGlobalByAddress(globalMemoryOffset));
                else
                    ArgumentTwo = context.MakeConstant(typeof(UnknownGlobalAddr), new UnknownGlobalAddr(globalMemoryOffset));
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            var display1 = ArgumentOne is LocalDefinition local1 ? local1 : ((ConstantDefinition) ArgumentOne)?.Value;
            var display2 = ArgumentTwo is LocalDefinition local2 ? local2 : ((ConstantDefinition) ArgumentTwo)?.Value;

            return $"[!] Compares {display1} and {display2}";
        }
    }
}