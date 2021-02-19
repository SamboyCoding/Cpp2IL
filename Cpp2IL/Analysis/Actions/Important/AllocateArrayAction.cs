using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class AllocateArrayAction : BaseAction
    {
        private readonly long sizeAllocated;
        private readonly TypeReference? arrayType;
        private readonly LocalDefinition? _localWritten;

        public AllocateArrayAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var typeConstant = !LibCpp2IlMain.ThePe!.is32Bit ? context.GetConstantInReg("rcx") : context.Stack.Peek() as ConstantDefinition;

            if (typeConstant != null && LibCpp2IlMain.ThePe.is32Bit)
                context.Stack.Pop(); //Pop off array type

            if (typeConstant == null) return;
            
            var sizeOperand = !LibCpp2IlMain.ThePe!.is32Bit ? context.GetOperandInRegister("rdx") : context.Stack.Peek();
            
            if (sizeOperand != null && LibCpp2IlMain.ThePe.is32Bit)
                context.Stack.Pop(); //Pop off array size

            if (sizeOperand == null) return;

            if (typeConstant.Value is TypeReference reference)
            {
                arrayType = reference;
            }

            if (sizeOperand is LocalDefinition {KnownInitialValue: ulong sizeL})
            {
                sizeAllocated = (long) sizeL;
            } else if (sizeOperand is ConstantDefinition {Value: ulong sizeC})
            {
                sizeAllocated = (long) sizeC;
            }

            if (arrayType == null) return; 

            _localWritten = context.MakeLocal(arrayType, reg: "rax", knownInitialValue: new AllocatedArray((int) sizeAllocated, (ArrayType) arrayType));
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(ILProcessor processor)
        {
            throw new System.NotImplementedException();
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