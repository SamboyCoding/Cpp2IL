using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class CallInitClassAction : BaseAction
    {
        public TypeReference theType;
        public CallInitClassAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            ConstantDefinition? consDef;
            if (LibCpp2IlMain.ThePe!.is32Bit)
            {
                consDef = context.Stack.Count > 0 ? context.Stack.Peek() as ConstantDefinition : null;
                if (consDef != null)
                    context.Stack.Pop();
            }
            else
                consDef = context.GetConstantInReg("rcx");

            if (consDef != null && consDef.Type == typeof(TypeReference))
                theType = (TypeReference) consDef.Value;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Calls the il2cpp cctor for the type {theType}";
        }
    }
}