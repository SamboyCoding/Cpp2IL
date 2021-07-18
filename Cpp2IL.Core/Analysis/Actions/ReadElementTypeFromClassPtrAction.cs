using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class ReadElementTypeFromClassPtrAction : BaseAction
    {
        private Il2CppClassIdentifier? _classPtr;
        private string? _destReg;

        public ReadElementTypeFromClassPtrAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            var readFromReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var readFrom = context.GetConstantInReg(readFromReg);

            _classPtr = readFrom?.Value as Il2CppClassIdentifier;
            
            if(_classPtr == null)
                return;
            
            //TODO: When we load the class pointer we load it as an Il2CppTypeDefinition which can't handle arrays - need to change to reflection data.
            //TODO: Then load the array element type here.
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
            return $"Reads the element type from the class pointer {_classPtr?.backingType} and stores in register {_destReg}";
        }
    }
}