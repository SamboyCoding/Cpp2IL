using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class SafeCastAction : BaseAction
    {
        private LocalDefinition? castSource;
        private TypeDefinition? destinationType;
        private ConstantDefinition? _castResult;

        public SafeCastAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var inReg = context.GetOperandInRegister("rcx");
            castSource = inReg is LocalDefinition local ? local : inReg is ConstantDefinition cons && cons.Value is NewSafeCastResult result ? result.original : null;
            var destOp = context.GetOperandInRegister("rdx");
            if (destOp is ConstantDefinition cons2 && cons2.Type == typeof(TypeDefinition))
                destinationType = (TypeDefinition) cons2.Value;

            if (destinationType == null || castSource == null) return;

            _castResult = context.MakeConstant(typeof(NewSafeCastResult), new NewSafeCastResult
            {
                castTo = destinationType,
                original = castSource
            }, reg: "rax");
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
            return $"Attempts to safely cast {castSource} to managed type {destinationType?.FullName} and stores the cast result in rax as {_castResult?.Name}";
        }
    }
}