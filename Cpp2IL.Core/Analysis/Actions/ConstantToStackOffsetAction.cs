using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class ConstantToStackOffsetAction : BaseAction<Instruction>
    {
        private readonly uint _stackOffset;
        private readonly ConstantDefinition? _sourceConstant;
        private readonly string? _sourceReg;
        private readonly LocalDefinition? _newLocal;

        public ConstantToStackOffsetAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _stackOffset = instruction.MemoryDisplacement32;
            _sourceReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            _sourceConstant = context.GetConstantInReg(_sourceReg);

            if (_sourceConstant == null) 
                return;
            
            _newLocal = context.MakeLocal(Utils.TryLookupTypeDefKnownNotGeneric(_sourceConstant.Type.FullName)!, knownInitialValue: _sourceConstant.Value);
            context.StackStoredLocals[(int) _stackOffset] = _newLocal;
            RegisterUsedLocal(_newLocal);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Moves {_sourceConstant?.GetPseudocodeRepresentation()} (type {_sourceConstant?.Type}) from register {_sourceReg} to the stack at offset {_stackOffset} (0x{_stackOffset:X}) as a new local {_newLocal?.Name}";
        }
    }
}