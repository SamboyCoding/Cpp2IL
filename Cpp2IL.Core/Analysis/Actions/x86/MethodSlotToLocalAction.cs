using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class MethodSlotToLocalAction : BaseAction<Instruction>
    {
        private ushort _slot;
        private LocalDefinition? _localMade;
        private MethodReference? _methodBeingRead;

        public MethodSlotToLocalAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var constantBeingRead = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (constantBeingRead?.Type != typeof(MethodReference))
            {
                if (constantBeingRead?.Value is GenericMethodReference gmr)
                    _methodBeingRead = gmr.Method;
                else
                    return;
            }
            else
            {
                _methodBeingRead = constantBeingRead.Value as MethodReference;
            }
            
            if(_methodBeingRead == null)
                return;

            if (_methodBeingRead.Resolve() != null)
                _slot = SharedState.ManagedToUnmanagedMethods[_methodBeingRead.Resolve()]?.slot ?? 0;

            if(_slot != 0)
                _localMade = context.MakeLocal(Utils.UInt32Reference, reg: Utils.GetRegisterNameNew(instruction.Op0Register), knownInitialValue: _slot);
            else
                _localMade = context.MakeLocal(Utils.UInt32Reference, reg: Utils.GetRegisterNameNew(instruction.Op0Register));
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
            return $"Reads the method slot for method {_methodBeingRead} (which is {_slot}) and stores in new local {_localMade}";
        }
    }
}