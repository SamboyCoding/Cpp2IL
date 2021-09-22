using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractExceptionThrowerAction<T> : BaseAction<T>
    {
        protected TypeDefinition? _exceptionType;

        protected AbstractExceptionThrowerAction(MethodAnalysis<T> context, T instruction) : base(context, instruction)
        {
        }

        public sealed override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (_exceptionType == null)
                throw new TaintedInstructionException();

            var ctor = _exceptionType.GetConstructors().FirstOrDefault(c => !c.HasParameters);

            if (ctor == null)
            {
                var exceptionCtor = Utils.ExceptionReference.GetConstructors().First(c => c.HasParameters && c.Parameters.Count == 1 && c.Parameters[0].ParameterType.Name == "String");
                return new[]
                {
                    processor.Create(OpCodes.Ldstr, $"Exception of type {_exceptionType.FullName}, but couldn't find a no-arg ctor"),
                    processor.Create(OpCodes.Newobj, processor.ImportReference(exceptionCtor)),
                    processor.Create(OpCodes.Throw)
                };
            }

            return new[]
            {
                processor.Create(OpCodes.Newobj, processor.ImportReference(ctor)),
                processor.Create(OpCodes.Throw)
            };
        }

        public sealed override string? ToPsuedoCode()
        {
            return $"throw new {_exceptionType}()";
        }

        public sealed override string ToTextSummary()
        {
            return $"[!] Constructs and throws an exception of kind {_exceptionType}\n";
        }

        public sealed override bool IsImportant()
        {
            return true;
        }
    }
}