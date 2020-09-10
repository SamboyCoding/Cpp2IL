using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    /// <summary>
    /// Used for error-checking, doesn't generate any pseudocode or IL
    /// </summary>
    public class AllocateInstanceAction : BaseAction
    {
        public TypeDefinition TypeCreated;
        public LocalDefinition LocalReturned;
        
        public AllocateInstanceAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var constant = context.GetConstantInReg("rcx");
            if (constant == null || constant.Type != typeof(TypeDefinition)) return;

            TypeCreated = (TypeDefinition) constant.Value;

            LocalReturned = context.MakeLocal(TypeCreated, reg: "rax");
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string ToPsuedoCode()
        {
            return "";
        }

        public override string ToTextSummary()
        {
            return $"Allocates an instance of type {TypeCreated} and stores it as {LocalReturned.Name} in rax.";
        }
    }
}