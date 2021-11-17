using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class StaticFieldOffsetToRegAction : BaseAction<Instruction>
    {
        private StaticFieldsPtr? _staticFieldPtrObject;
        private string? _destReg;
        private ConstantDefinition? _constantMade;

        public StaticFieldOffsetToRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //Get the type we're moving from
            var theConstant = context.GetConstantInReg(MiscUtils.GetRegisterNameNew(instruction.MemoryBase));
            _destReg = MiscUtils.GetRegisterNameNew(instruction.Op0Register);

            if (theConstant == null || theConstant.Type != typeof(TypeReference)) return;

            var typeFieldsAreFor = (TypeReference) theConstant.Value;
            _staticFieldPtrObject = new StaticFieldsPtr(typeFieldsAreFor);

            _constantMade = context.MakeConstant(typeof(StaticFieldsPtr), _staticFieldPtrObject, reg: _destReg);
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
            return $"Loads the pointer to the static fields for {_staticFieldPtrObject?.TypeTheseFieldsAreFor.FullName} and stores it in {_constantMade?.Name} in register {_destReg}";
        }
    }
}