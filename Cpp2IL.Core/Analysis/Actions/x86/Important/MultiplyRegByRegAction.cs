using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class MultiplyRegByRegAction: BaseAction<Instruction>
    {
        private string _operandOneReg;
        private string _operandZeroReg;
        private LocalDefinition? _operandOneLocal;
        private LocalDefinition? _operandZeroLocal;
        private LocalDefinition? _localMade;
        public MultiplyRegByRegAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _operandOneReg = Utils.Utils.GetRegisterNameNew(instruction.Op1Register);
            _operandZeroReg = Utils.Utils.GetRegisterNameNew(instruction.Op0Register);
            _operandOneLocal = context.GetLocalInReg(_operandOneReg);
            _operandZeroLocal = context.GetLocalInReg(_operandZeroReg);
            
            if(_operandZeroLocal?.Type == null || _operandOneLocal == null)
                return;
            
            RegisterUsedLocal(_operandZeroLocal, context);
            RegisterUsedLocal(_operandOneLocal, context);

            _localMade = context.MakeLocal(_operandZeroLocal.Type, reg: _operandZeroReg);

        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            if (_operandZeroLocal == null || _operandOneLocal == null)
                throw new TaintedInstructionException("Missing at least one local operand");

            if (_localMade?.Variable == null)
                throw new TaintedInstructionException("Local made was stripped");

            List<Mono.Cecil.Cil.Instruction> result = new ();
            
            result.AddRange(_operandZeroLocal.GetILToLoad(context, processor));
            result.AddRange(_operandOneLocal.GetILToLoad(context, processor));
            result.Add(processor.Create(OpCodes.Mul));
            result.Add(processor.Create(OpCodes.Stloc, _localMade.Variable));

            return result.ToArray();
        }

        public override bool IsImportant() => true;

        public override string? ToPsuedoCode()
        {
            return $"{_operandZeroLocal?.Type?.FullName} {_localMade?.Name} = {_operandZeroLocal?.Name} * {_operandOneLocal?.Name}";
        }

        public override string ToTextSummary()
        {
            return $"Multiplies {_operandZeroLocal?.Name} ({_operandZeroReg}) by {_operandOneLocal?.Name} ({_operandOneReg}) and stores the result in {_localMade?.Name} ({_operandZeroReg})";
        }
    }
}