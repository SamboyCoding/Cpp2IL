using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ImplicitCreateReturnValueInX8Action : BaseAction<Arm64Instruction>
    {
        private TypeReference _returnType;
        private LocalDefinition _localDefinition;

        public Arm64ImplicitCreateReturnValueInX8Action(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _returnType = context.ReturnType;

            _localDefinition = context.MakeLocal(_returnType, "functionReturnVal", "x8");
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            if (_returnType.HasGenericParameters || _returnType is GenericInstanceType)
                throw new TaintedInstructionException("Not implemented for generic types");

            var ctor = _returnType.Resolve().Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);

            if (ctor == null)
                throw new TaintedInstructionException("Not implemented for types with a complex constructor");

            if (_localDefinition.Variable == null)
                throw new TaintedInstructionException("Return value variable has been stripped");

            return new[]
            {
                processor.Create(OpCodes.Newobj, ctor),
                processor.Create(OpCodes.Stloc, _localDefinition.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            return $"{_returnType} {_localDefinition.Name} = new {_returnType}()";
        }

        public override string ToTextSummary()
        {
            return $"[!] Implicitly creates an instance of the struct {_returnType} as a local {_localDefinition.Name} in x8 for the return value of the function";
        }

        public override bool IsImportant() => true;
    }
}