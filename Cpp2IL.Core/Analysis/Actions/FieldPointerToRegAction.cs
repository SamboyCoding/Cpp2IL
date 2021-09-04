using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class FieldPointerToRegAction : BaseAction<Instruction>
    {
        private FieldUtils.FieldBeingAccessedData? _fieldBeingRead;
        private string? _destReg;
        private LocalDefinition? _accessedOn;

        public FieldPointerToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            if (instruction.MemoryBase != Register.None)
            {
                var memoryBase = Utils.GetRegisterNameNew(instruction.MemoryBase);

                _accessedOn = context.GetLocalInReg(memoryBase);
                if (_accessedOn?.Type == null)
                    return;

                _fieldBeingRead = FieldUtils.GetFieldBeingAccessed(_accessedOn.Type, instruction.MemoryDisplacement64, false);
            }
            else
            {
                //Add?
                var amountBeingAdded = instruction.GetImmediate(1);
                _accessedOn = context.GetLocalInReg(Utils.GetRegisterNameNew(instruction.Op0Register));
                
                if (_accessedOn?.Type == null)
                    return;
                
                _fieldBeingRead = FieldUtils.GetFieldBeingAccessed(_accessedOn.Type, amountBeingAdded, false);
            }

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            
            if(_fieldBeingRead == null)
                return;

            context.MakeConstant(typeof(FieldPointer), new FieldPointer(_fieldBeingRead, _accessedOn), reg: _destReg);
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
            return $"Loads the pointer to the field {_fieldBeingRead} on {_accessedOn} into register {_destReg}";
        }
    }
}