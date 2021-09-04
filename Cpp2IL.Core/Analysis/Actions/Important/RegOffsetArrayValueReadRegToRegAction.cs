using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class RegOffsetArrayValueReadRegToRegAction : BaseAction<Instruction>
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
            
            RegisterUsedLocal(_arrayLocal);

            _indexLocal = context.GetLocalInReg(indexReg);
            
            if(_indexLocal != null)
                RegisterUsedLocal(_indexLocal);
            
            //Regardless of if we have an index local, we can still work out the type of the array and make a local.
            //Resolve() turns array types into non-array types
            _destLocal = context.MakeLocal(_arrayLocal.Type.Resolve(), reg: destinationReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
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