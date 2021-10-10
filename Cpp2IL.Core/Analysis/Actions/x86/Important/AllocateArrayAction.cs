using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class AllocateArrayAction : AbstractArrayAllocationAction<Instruction>
    {
        public AllocateArrayAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
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
                TypeOfArray = reference;
            }

            if (sizeOperand is LocalDefinition local && (local.KnownInitialValue is ulong || local.KnownInitialValue is uint))
            {
                RegisterUsedLocal(local, context);
                SizeAllocated = Convert.ToInt32(local.KnownInitialValue);
            }
            else if (sizeOperand is ConstantDefinition {Value: ulong sizeC})
            {
                SizeAllocated = (int) sizeC;
            } else if (sizeOperand is ConstantDefinition {Value: uint sizeCSmall})
            {
                SizeAllocated = (int) sizeCSmall;
            }

            if (TypeOfArray is not ArrayType arrayType) return;

            LocalWritten = context.MakeLocal(arrayType, reg: "rax", knownInitialValue: new AllocatedArray(SizeAllocated, arrayType));
            RegisterUsedLocal(LocalWritten, context); //Used implicitly until I can find out what's causing these issues
        }
    }
}