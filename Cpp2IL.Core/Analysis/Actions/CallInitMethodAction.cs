using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class CallInitMethodAction : BaseAction
    {
        private UnknownGlobalAddr? _globalAddr;
        private int functionId;

        public CallInitMethodAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            ConstantDefinition? consDef;
            if (LibCpp2IlMain.Binary!.is32Bit)
            {
                consDef = context.Stack.Count > 0 ? context.Stack.Peek() as ConstantDefinition : null;
                if (consDef != null)
                    context.Stack.Pop();
            }
            else
                consDef = context.GetConstantInReg("rcx");

            if (consDef != null && consDef.Type == typeof(UnknownGlobalAddr))
            {
                _globalAddr = (UnknownGlobalAddr) consDef.Value;
                functionId = (int) Utils.GetNumericConstant(_globalAddr.addr, Utils.Int32Reference);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Attempts to load the il2cpp metadata for this method (method id {functionId}) and init it cpp-side.\n";
        }
    }
}