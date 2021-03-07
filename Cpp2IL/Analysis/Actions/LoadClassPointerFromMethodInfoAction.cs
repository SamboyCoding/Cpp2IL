using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.Metadata;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LoadClassPointerFromMethodInfoAction : BaseAction
    {
        private Il2CppTypeDefinition? _type;
        private string? _destReg;
        private ConstantDefinition? _constantMade;

        public LoadClassPointerFromMethodInfoAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //Already verified the source type and offset, only need to handle destination
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            //The klass is the declaring type of this method.
            _type = SharedState.ManagedToUnmanagedTypes[context.DeclaringType];

            _constantMade = context.MakeConstant(typeof(Il2CppClassIdentifier), new Il2CppClassIdentifier
            {
                objectAlias = "this",
                backingType = _type 
            }, reg: _destReg);
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
            return $"Reads the klass pointer representing the declaring type of this method (which is {_type?.FullName}) and stores in new constant {_constantMade?.Name} in register {_destReg}";
        }
    }
}