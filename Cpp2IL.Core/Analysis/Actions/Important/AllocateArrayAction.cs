using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class AllocateArrayAction : BaseAction<Instruction>
    {
        private readonly int sizeAllocated;
        private readonly TypeReference? typeOfArray;
        private readonly LocalDefinition<Instruction>? _localWritten;

        public AllocateArrayAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var typeConstant = !LibCpp2IlMain.Binary!.is32Bit ? context.GetConstantInReg("rcx") : context.Stack.Peek() as ConstantDefinition<Instruction>;

            if (typeConstant != null && LibCpp2IlMain.Binary.is32Bit)
                context.Stack.Pop(); //Pop off array type

            if (typeConstant == null) return;

            var sizeOperand = !LibCpp2IlMain.Binary!.is32Bit ? context.GetOperandInRegister("rdx") : context.Stack.Peek();

            if (sizeOperand != null && LibCpp2IlMain.Binary.is32Bit)
                context.Stack.Pop(); //Pop off array size

            if (sizeOperand == null) return;

            if (typeConstant.Value is TypeReference reference)
            {
                typeOfArray = reference;
            }

            if (sizeOperand is LocalDefinition<Instruction> local && (local.KnownInitialValue is ulong || local.KnownInitialValue is uint))
            {
                RegisterUsedLocal(local);
                sizeAllocated = Convert.ToInt32(local.KnownInitialValue);
            }
            else if (sizeOperand is ConstantDefinition<Instruction> {Value: ulong sizeC})
            {
                sizeAllocated = (int) sizeC;
            } else if (sizeOperand is ConstantDefinition<Instruction> {Value: uint sizeCSmall})
            {
                sizeAllocated = (int) sizeCSmall;
            }

            if (!(typeOfArray is ArrayType arrayType)) return;

            _localWritten = context.MakeLocal(arrayType, reg: "rax", knownInitialValue: new AllocatedArray(sizeAllocated, arrayType));
            RegisterUsedLocal(_localWritten); //Used implicitly until I can find out what's causing these issues
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_localWritten == null || typeOfArray == null)
                throw new TaintedInstructionException("Missing created local or type of array");

            if (!(typeOfArray is ArrayType arrayType))
                throw new TaintedInstructionException("Array type isn't an array");

            return new []
            {
                processor.Create(OpCodes.Ldc_I4, sizeAllocated),
                processor.Create(OpCodes.Newarr, processor.ImportReference(arrayType.ElementType)),
                processor.Create(OpCodes.Stloc, _localWritten.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            var aType = typeOfArray as ArrayType;
            return $"{typeOfArray?.FullName} {_localWritten?.Name} = new {aType?.ElementType}[{sizeAllocated}]";
        }

        public override string ToTextSummary()
        {
            if (!(typeOfArray is ArrayType))
                return $"[!!] Allocates an array of a type which isn't an array (got {typeOfArray}), of size {sizeAllocated}, and stores the result as {_localWritten?.Name}. This is a problem - we couldn't resolve the array type";
            
            return $"[!] Allocates an array of type {typeOfArray?.FullName} of size {sizeAllocated} and stores the result as {_localWritten?.Name} in register rax\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}