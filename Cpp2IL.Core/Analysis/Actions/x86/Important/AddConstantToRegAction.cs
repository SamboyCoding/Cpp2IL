using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class AddConstantToRegAction : BaseAction<Instruction>
    {
        private string _regBeingAddedTo;
        private LocalDefinition? _valueInReg;
        private ulong _constantBeingAdded;

        public AddConstantToRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _regBeingAddedTo = MiscUtils.GetRegisterNameNew(instruction.Op0Register);
            _valueInReg = context.GetLocalInReg(_regBeingAddedTo);
            
            //Handle INC instructions here too.
            _constantBeingAdded = instruction.Mnemonic == Mnemonic.Inc ? 1 : instruction.GetImmediate(1); 

            if (_valueInReg?.Type == null) return;
            
            RegisterUsedLocal(_valueInReg, context);

            if (!MiscUtils.IsNumericType(_valueInReg.Type))
            {
                AddComment("Type being added to is non-numeric!");
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_valueInReg?.Type is null)
                throw new TaintedInstructionException("Local being added to is null / doesn't have a type");

            var instructions = new List<Mono.Cecil.Cil.Instruction>(); 
            
            instructions.AddRange(_valueInReg.GetILToLoad(context, processor));

            var typeAddingTo = typeof(int).Module.GetType(_valueInReg.Type.FullName);

            if (typeAddingTo == null)
                throw new TaintedInstructionException($"Value in reg is {_valueInReg} with type {_valueInReg.Type}, which we can't find in the system, so can't cast our constant {_constantBeingAdded} to");

            if (_valueInReg?.Variable == null)
                throw new TaintedInstructionException("Value is reg has no variable. Stripped? Or a function param?");

            if (typeAddingTo == typeof(int)) 
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(uint))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(ulong))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(long))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(byte))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(float))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(double))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(char))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else if (typeAddingTo == typeof(sbyte))
                instructions.AddRange(context.MakeConstant(typeAddingTo, _constantBeingAdded).GetILToLoad(context, processor));
            else 
                throw new TaintedInstructionException($"Don't know how to create a suitable constant for type: {typeAddingTo.FullName} to add to local");

            instructions.Add(processor.Create(OpCodes.Add));
            
            instructions.Add(processor.Create(OpCodes.Stloc, _valueInReg.Variable));

            return instructions.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_valueInReg?.Name} += {_constantBeingAdded}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Adds {_constantBeingAdded} to the value {_valueInReg}, stored in {_regBeingAddedTo}";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}