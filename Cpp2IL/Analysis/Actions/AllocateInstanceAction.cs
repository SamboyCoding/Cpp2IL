using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    /// <summary>
    /// Used for error-checking, doesn't generate any pseudocode or IL
    /// </summary>
    public class ActionAllocateInstance : BaseAction
    {
        public TypeDefinition TypeCreated;
        
        public ActionAllocateInstance(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
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
            throw new System.NotImplementedException();
        }
    }
}