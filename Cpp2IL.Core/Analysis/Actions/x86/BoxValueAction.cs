using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class BoxValueAction : BaseAction<Instruction>
    {
        private TypeReference? destinationType;
        private IAnalysedOperand? primitiveObject;
        private LocalDefinition? _localMade;

        public BoxValueAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //TODO 32-bit, stack is different, need to pop instead of rdx, etc.
            primitiveObject = context.GetLocalInReg("rdx");

            if (primitiveObject == null && !LibCpp2IlMain.Binary!.is32Bit)
                //Try stack pointer
                primitiveObject = context.GetConstantInReg("rdx") is {Value: StackPointer pointer} && context.StackStoredLocals.TryGetValue((int) pointer.offset, out var loc) ? loc : null;

            var destinationConstant = context.GetConstantInReg("rcx");

            destinationType = destinationConstant?.Value as TypeReference;
            
            if(destinationType == null || primitiveObject == null)
                return;

            var value = primitiveObject switch
            {
                LocalDefinition loc => loc.KnownInitialValue,
                ConstantDefinition con => con.Value,
                _ => null
            };
            
            if(value == null)
                return;

            try
            {
                value = Utils.CoerceValue(value, destinationType);
            }
            catch (Exception)
            {
                return;
            }

            _localMade = context.MakeLocal(destinationType, reg: "rax", knownInitialValue: value);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Boxes a cpp primitive value {primitiveObject} to managed type {destinationType?.FullName} and stores the result in new local {_localMade?.Name} in register rax.";
        }
    }
}