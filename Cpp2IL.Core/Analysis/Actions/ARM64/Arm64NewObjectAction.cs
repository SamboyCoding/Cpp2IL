using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64NewObjectAction : AbstractNewObjAction<Arm64Instruction>
    {
        private TypeReference? _type;

        public Arm64NewObjectAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction associatedInstruction) : base(context, associatedInstruction)
        {
            var typeConstant = context.GetConstantInReg("x0");
            TypeCreated = typeConstant?.Value as TypeReference;
            
            if(TypeCreated == null)
                return;

            LocalReturned = context.MakeLocal(TypeCreated, reg: "x0");
            
            RegisterUsedLocal(LocalReturned);
        }
    }
}