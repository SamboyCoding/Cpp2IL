using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractComparisonAction<T> : BaseAction<T>
    {
        public IComparisonArgument<T>? ArgumentOne;
        public IComparisonArgument<T>? ArgumentTwo;
        
        public bool UnimportantComparison;
        public ulong EndOfLoopAddr;

        protected AbstractComparisonAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }
        
        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            var display1 = ArgumentOne?.ToString();
            var display2 = ArgumentTwo?.ToString();

            //Only show the important [!] if this is an important comparison (i.e. not an il2cpp one)
            return UnimportantComparison ? $"Compares {display1} and {display2}" : $"[!] Compares {display1} and {display2}";
        }
        
        internal bool IsProbablyWhileLoop() => EndOfLoopAddr != 0;
    }
}