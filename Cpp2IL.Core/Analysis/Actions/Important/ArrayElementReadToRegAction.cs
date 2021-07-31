using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ArrayElementReadToRegAction : BaseAction
    {
        private readonly string? _arrayReg;
        private readonly string? _offsetReg;
        private readonly LocalDefinition? _arrayLocal;
        private readonly LocalDefinition? _offsetLocal;
        private readonly ArrayType? _arrType;
        private readonly string? _destReg;
        public readonly LocalDefinition? LocalMade;
        private TypeReference? _elemType;

        public ArrayElementReadToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _arrayReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _offsetReg = Utils.GetRegisterNameNew(instruction.MemoryIndex);

            _arrayLocal = context.GetLocalInReg(_arrayReg);
            _offsetLocal = context.GetLocalInReg(_offsetReg);
            
            if(_arrayLocal?.Type?.IsArray != true)
                return;

            _arrType = (ArrayType) _arrayLocal.Type;
            
            if(_arrType == null)
                return;

            _elemType = _arrType.GetElementType();

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            LocalMade = context.MakeLocal(_arrType.ElementType, reg: _destReg);
            
            RegisterUsedLocal(_arrayLocal);
            
            if(_offsetLocal != null)
                RegisterUsedLocal(_offsetLocal);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (LocalMade == null || _arrayLocal == null || _offsetLocal == null)
                throw new TaintedInstructionException("Array, offset, or destination is null");

            if (LocalMade.Variable == null)
                //Stripped out - couldn't find a usage for this local.
                return Array.Empty<Mono.Cecil.Cil.Instruction>();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            //Load array
            ret.AddRange(_arrayLocal.GetILToLoad(context, processor));
            
            //Load index
            ret.AddRange(_offsetLocal.GetILToLoad(context, processor));

            //Pop offset and array, push element
            ret.Add(processor.Create(OpCodes.Ldelem_Any, processor.ImportReference(_elemType!)));
            
            //Store item in local
            ret.Add(processor.Create(OpCodes.Stloc, LocalMade.Variable));

            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{_arrType?.ElementType} {LocalMade?.GetPseudocodeRepresentation()} = {_arrayLocal?.GetPseudocodeRepresentation()}[{_offsetLocal?.GetPseudocodeRepresentation()}]";
        }

        public override string ToTextSummary()
        {
            return $"Copies the element in the array {_arrayLocal} (stored in register {_arrayReg}) at the index specified by {_offsetLocal} (stored in register {_offsetReg}) into new local {LocalMade} in register {_destReg}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}