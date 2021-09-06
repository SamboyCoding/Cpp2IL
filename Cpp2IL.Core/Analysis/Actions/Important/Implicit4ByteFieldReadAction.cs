using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class Implicit4ByteFieldReadAction : BaseAction<Instruction>
    {
        private FieldUtils.FieldBeingAccessedData? _read;
        private LocalDefinition<Instruction>? _readOn;
        private LocalDefinition<Instruction>? _localMade;

        public Implicit4ByteFieldReadAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _readOn = context.GetLocalInReg(Utils.GetRegisterNameNew(instruction.Op1Register));
            
            if(_readOn == null)
                return;
            
            RegisterUsedLocal(_readOn);
            _read = FieldUtils.GetFieldBeingAccessed(_readOn.Type!, 0, false);
            
            if(_read == null)
                return;

            var type = _read.GetFinalType();

            if (type is GenericParameter p && _readOn.Type is GenericInstanceType git)
            {
                type = GenericInstanceUtils.GetGenericArgumentByNameFromGenericInstanceType(git, p);
                type ??= _read.GetFinalType();
            }

            _localMade = context.MakeLocal(type, reg: Utils.GetRegisterNameNew(instruction.Op0Register));
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_localMade == null || _readOn == null || _read == null)
                throw new TaintedInstructionException("Missing the object being read from, or the field being read.");

            if (_localMade.Variable == null)
                throw new TaintedInstructionException($"Local {_localMade.Name} has been stripped out for being unused, so doesn't have a variable.");
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            //Load object
            ret.AddRange(_readOn.GetILToLoad(context, processor));

            //Access field
            ret.AddRange(_read.GetILToLoad(processor));

            //Store to local
            ret.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));
            
            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type} {_localMade?.Name} = {_readOn?.GetPseudocodeRepresentation()}.{_read}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Implicitly reads field at offset 0 (which is {_read}) from struct {_readOn?.Name} of type {_readOn?.Type} and stores in new local {_localMade}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}