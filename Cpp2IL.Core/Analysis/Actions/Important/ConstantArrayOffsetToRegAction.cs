using System;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ConstantArrayOffsetToRegAction : BaseAction<Instruction>
    {
        private readonly LocalDefinition? _arrayLocal;
        private readonly int _index;
        private readonly LocalDefinition? _destLocal;
        private TypeReference? _elementType;

        public ConstantArrayOffsetToRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //God knows why the memory *index* contains the array, and the base contains the index, but it does.
            var arrayContainingReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var destinationReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            var arrayOffset = instruction.MemoryDisplacement;

            _arrayLocal = context.GetLocalInReg(arrayContainingReg);

            if (_arrayLocal?.Type?.IsArray != true) return;
            
            RegisterUsedLocal(_arrayLocal);

            _index = (int) ((arrayOffset - Il2CppArrayUtils.FirstItemOffset) / Utils.GetPointerSizeBytes());
            
            //Regardless of if we have an index local, we can still work out the type of the array and make a local.
            //Resolve() turns array types into non-array types

            _elementType = _arrayLocal.Type is ArrayType at ? at.ElementType : _arrayLocal.Type.Resolve();
            
            _destLocal = context.MakeLocal(_elementType, reg: destinationReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_destLocal == null || _arrayLocal == null || _index < 0)
                throw new TaintedInstructionException();

            if (_destLocal.Variable == null)
                //Stripped out - couldn't find a usage for this local.
                return Array.Empty<Mono.Cecil.Cil.Instruction>();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            //Load array
            ret.AddRange(_arrayLocal.GetILToLoad(context, processor));
            
            //Load offset
            ret.Add(processor.Create(OpCodes.Ldc_I4, _index));
            
            //Pop offset and array, push element
            ret.Add(processor.Create(OpCodes.Ldelem_Any, processor.ImportReference(_elementType)));
            
            //Store in local
            ret.Add(processor.Create(OpCodes.Stloc, _destLocal.Variable));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_destLocal?.Type?.FullName} {_destLocal?.Name} = {_arrayLocal?.Name}[{_index}]";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads a value from the array {_arrayLocal} at index {_index}, into a new local {_destLocal}\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}