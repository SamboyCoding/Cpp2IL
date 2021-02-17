using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class ConstantArrayOffsetToRegAction : BaseAction
    {
        private readonly LocalDefinition? _arrayLocal;
        private readonly int _index;
        private readonly LocalDefinition? _destLocal;

        public ConstantArrayOffsetToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            //God knows why the memory *index* contains the array, and the base contains the index, but it does.
            var arrayContainingReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destinationReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            var arrayOffset = instruction.MemoryDisplacement;

            _arrayLocal = context.GetLocalInReg(arrayContainingReg);

            if (_arrayLocal?.Type?.IsArray != true) return;

            _index = (int) ((arrayOffset - Il2CppArrayUtils.FirstItemOffset) / Utils.GetPointerSizeBytes());
            
            //Regardless of if we have an index local, we can still work out the type of the array and make a local.
            //Resolve() turns array types into non-array types

            var elementType = _arrayLocal.Type is ArrayType at ? at.ElementType : _arrayLocal.Type.Resolve();
            
            _destLocal = context.MakeLocal(elementType, reg: destinationReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_destLocal?.Type?.FullName} {_destLocal?.Name} = {_arrayLocal?.Name}[{_index}]";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads a value from the array {_arrayLocal} at index {_index}, into a new local {_destLocal}\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}