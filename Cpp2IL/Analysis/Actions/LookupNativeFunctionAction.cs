using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LookupNativeFunctionAction : BaseAction
    {
        private string methodName;
        private MethodDefinition? resolvedMethod;
        
        public LookupNativeFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
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
            return $"Looks up native method by name \"{methodName}\" which resolves to {resolvedMethod?.FullName}";
        }
    }
}