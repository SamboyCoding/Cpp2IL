using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LocalToStackOffsetAction : BaseAction
    {
        private uint _stackOffset;
        private LocalDefinition? _sourceLocal;
        private string? _sourceReg;

        public LocalToStackOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _stackOffset = instruction.MemoryDisplacement32;
            _sourceReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            _sourceLocal = context.GetLocalInReg(_sourceReg);

            if (_sourceLocal == null) 
                return;
            
            context.StackStoredLocals[(int) _stackOffset] = _sourceLocal;
            RegisterUsedLocal(_sourceLocal);
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
            return $"Moves {_sourceLocal?.GetPseudocodeRepresentation()} (type {_sourceLocal?.Type}) from register {_sourceReg} to the stack at offset {_stackOffset}";
        }
    }
}