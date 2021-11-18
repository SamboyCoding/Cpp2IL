using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
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
        private bool _boxingFieldPointer = false;
        private FieldPointer _boxedField;

        public BoxValueAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //TODO 32-bit, stack is different, need to pop instead of rdx, etc.
            primitiveObject = context.GetLocalInReg("rdx");

            if (primitiveObject == null && !LibCpp2IlMain.Binary!.is32Bit)
                //Try stack pointer
                primitiveObject = context.GetConstantInReg("rdx") is {Value: StackPointer pointer} && context.StackStoredLocals.TryGetValue((int) pointer.offset, out var loc) ? loc : null;
            
            if (primitiveObject == null && !LibCpp2IlMain.Binary!.is32Bit)
            {
                var constantDefinition = context.GetConstantInReg("rdx");
                if (constantDefinition is {Value: FieldPointer fieldPointer})
                {
                    primitiveObject = constantDefinition;
                    _boxingFieldPointer = true;
                    _boxedField = fieldPointer;
                }
            }

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
                if (!_boxingFieldPointer)
                {
                    value = AnalysisUtils.CoerceValue(value, destinationType);
                    _localMade = context.MakeLocal(destinationType, reg: "rax", knownInitialValue: value);
                }
                else
                {
                    _localMade = context.MakeLocal(destinationType, reg: "rax");
                }
            }
            catch (Exception)
            {
                //Ignore
            }
        }
        
        public override bool IsImportant() => _boxingFieldPointer;

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (!_boxingFieldPointer)
                throw new NotImplementedException("This shouldn't have happened");

            
            if (_localMade == null || _boxedField?.OnWhat == null || _boxedField?.Field == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            //Load object
            ret.AddRange(_boxedField.OnWhat.GetILToLoad(context, processor));

            //Access field
            ret.AddRange(_boxedField.Field.GetILToLoad(processor));

            //Store to local
            ret.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));
            
            return ret.ToArray();
            
        }

        public override string? ToPsuedoCode()
        {
            if (_boxingFieldPointer)
                return $"{_localMade.Name} = {_boxedField.Field}";
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Boxes a cpp primitive {(_boxingFieldPointer?"field":"value")} {primitiveObject} to managed type {destinationType?.FullName} and stores the result in new local {_localMade?.Name} in register rax.";
        }
    }
}