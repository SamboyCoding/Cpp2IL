using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64StaticFieldOffsetToRegAction : BaseAction<Arm64Instruction>
    {
        private StaticFieldsPtr? _staticFieldPtrObject;
        private string? _destReg;
        private ConstantDefinition? _constantMade;

        public Arm64StaticFieldOffsetToRegAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            //Get the type we're moving from
            var theConstant = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id));
            _destReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            if (theConstant == null || theConstant.Type != typeof(TypeReference)) return;

            var typeFieldsAreFor = (TypeReference)theConstant.Value;
            _staticFieldPtrObject = new StaticFieldsPtr(typeFieldsAreFor);

            _constantMade = context.MakeConstant(typeof(StaticFieldsPtr), _staticFieldPtrObject, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
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