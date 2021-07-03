using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class AllocateArrayAction : BaseAction
    {
        private readonly int sizeAllocated;
        private readonly TypeReference? arrayType;
        private readonly LocalDefinition? _localWritten;

        public AllocateArrayAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var typeConstant = !LibCpp2IlMain.Binary!.is32Bit ? context.GetConstantInReg("rcx") : context.Stack.Peek() as ConstantDefinition;

            if (typeConstant != null && LibCpp2IlMain.Binary.is32Bit)
                context.Stack.Pop(); //Pop off array type

            if (typeConstant == null) return;

            var sizeOperand = !LibCpp2IlMain.Binary!.is32Bit ? context.GetOperandInRegister("rdx") : context.Stack.Peek();

            if (sizeOperand != null && LibCpp2IlMain.Binary.is32Bit)
                context.Stack.Pop(); //Pop off array size

            if (sizeOperand == null) return;

            if (typeConstant.Value is TypeReference reference)
            {
                arrayType = reference;
            }

            if (sizeOperand is LocalDefinition {KnownInitialValue: ulong sizeL} local)
            {
                RegisterUsedLocal(local);
                sizeAllocated = (int) sizeL;
            }
            else if (sizeOperand is ConstantDefinition {Value: ulong sizeC})
            {
                sizeAllocated = (int) sizeC;
            } else if (sizeOperand is ConstantDefinition {Value: uint sizeCSmall})
            {
                sizeAllocated = (int) sizeCSmall;
            }

            if (arrayType == null) return;

            _localWritten = context.MakeLocal(arrayType, reg: "rax", knownInitialValue: new AllocatedArray(sizeAllocated, (ArrayType) arrayType));
            RegisterUsedLocal(_localWritten); //Used implicitly until I can find out what's causing these issues
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (_localWritten == null || arrayType == null)
                throw new TaintedInstructionException("Missing created local or type of array");

            if (!(arrayType is ArrayType actualArrayType))
                throw new TaintedInstructionException("Array type isn't an array");

            return new []
            {
                processor.Create(OpCodes.Ldc_I4, sizeAllocated),
                processor.Create(OpCodes.Newarr, actualArrayType.ElementType),
                processor.Create(OpCodes.Stloc, _localWritten.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            var aType = arrayType as ArrayType;
            return $"{arrayType?.FullName} {_localWritten?.Name} = new {aType?.ElementType}[{sizeAllocated}]";
        }

        public override string ToTextSummary()
        {
            return $"[!] Allocates an array of type {arrayType?.FullName} of size {sizeAllocated} and stores the result as {_localWritten?.Name} in register rax\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}