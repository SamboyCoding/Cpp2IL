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
            var ripBasedAddr = instruction.GetRipBasedInstructionMemoryAddress();

            if (r0 != "rsp")
                ArgumentOne = instruction.Op0Kind == OpKind.Register
                    ? context.GetOperandInRegister(r0)
                    : instruction.Op0Kind.IsImmediate()
                        ? context.MakeConstant(typeof(int), instruction.GetImmediate(0))
                        : LibCpp2IlMain.GetAnyGlobalByAddress(ripBasedAddr).Offset == ripBasedAddr
                            ? context.MakeConstant(typeof(GlobalIdentifier), LibCpp2IlMain.GetAnyGlobalByAddress(ripBasedAddr))
                            : context.MakeConstant(typeof(UnknownGlobalAddr), new UnknownGlobalAddr(ripBasedAddr));
            if (r1 != "rsp")
                ArgumentTwo = instruction.Op1Kind == OpKind.Register
                    ? context.GetOperandInRegister(r1)
                    : instruction.Op1Kind.IsImmediate()
                        ? context.MakeConstant(typeof(int), instruction.GetImmediate(1))
                        :  LibCpp2IlMain.GetAnyGlobalByAddress(ripBasedAddr).Offset == ripBasedAddr
                            ? context.MakeConstant(typeof(GlobalIdentifier), LibCpp2IlMain.GetAnyGlobalByAddress(ripBasedAddr))
                            : context.MakeConstant(typeof(UnknownGlobalAddr), new UnknownGlobalAddr(ripBasedAddr));
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