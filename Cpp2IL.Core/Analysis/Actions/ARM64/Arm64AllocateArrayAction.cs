using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64AllocateArrayAction : AbstractArrayAllocationAction<Arm64Instruction>
    {
        public Arm64AllocateArrayAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction associatedInstruction) : base(context, associatedInstruction)
        {
            //X0 => type of array
            //X1 => size
            //Return array in x0
            var typeConstant = context.GetConstantInReg("x0");

            if (typeConstant == null) 
                return;
            
            if (typeConstant.Value is TypeReference reference)
            {
                TypeOfArray = reference;
            }

            var sizeOperand = context.GetOperandInRegister("x1");

            if (sizeOperand == null) 
                return;

            if (sizeOperand is LocalDefinition {KnownInitialValue: ulong or uint} local)
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
            } else if (sizeOperand is ConstantDefinition {Value: int sizeCSmallUnsigned})
            {
                SizeAllocated = sizeCSmallUnsigned;
            }
            else if (sizeOperand is LocalDefinition localDefinition)
            {
                LocalArraySize = true;
                LocalUsedForArraySize = localDefinition;
                RegisterUsedLocal(localDefinition, context);
            }

            if (TypeOfArray is not ArrayType arrayType) 
                return;

            LocalWritten = context.MakeLocal(arrayType, reg: "x0", knownInitialValue: new AllocatedArray(SizeAllocated, arrayType));
            RegisterUsedLocal(LocalWritten, context); //Used implicitly until I can find out what's causing these issues
        }
    }
}