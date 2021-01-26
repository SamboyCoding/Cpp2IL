using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class RegOffsetArrayValueReadRegToRegAction : BaseAction
    {
        private LocalDefinition? _arrayLocal;
        private LocalDefinition? _indexLocal;
        private LocalDefinition? _destLocal;

        public RegOffsetArrayValueReadRegToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //God knows why the memory *index* contains the array, and the base contains the index, but it does.
            var arrayContainingReg = Utils.GetRegisterNameNew(instruction.MemoryIndex);
            var indexReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destinationReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            _arrayLocal = context.GetLocalInReg(arrayContainingReg);

            if (_arrayLocal?.Type?.IsArray != true) return;

            _indexLocal = context.GetLocalInReg(indexReg);
            
            //Regardless of if we have an index local, we can still work out the type of the array and make a local.
            //Resolve() turns array types into non-array types
            _destLocal = context.MakeLocal(_arrayLocal.Type.Resolve(), reg: destinationReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_destLocal?.Type?.FullName} {_destLocal?.Name} = {_arrayLocal?.Name}[{_indexLocal?.Name}]";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads a value from the array {_arrayLocal} at an index specified by the value in {_indexLocal}, into a new local {_destLocal}\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}