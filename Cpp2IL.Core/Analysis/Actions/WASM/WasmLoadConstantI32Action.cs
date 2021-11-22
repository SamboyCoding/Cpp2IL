using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using WasmDisassembler;

namespace Cpp2IL.Core.Analysis.Actions.WASM
{
    public class WasmLoadConstantI32Action : BaseAction<WasmInstruction>
    {
        private int _theValue;
        private ConstantDefinition _constantMade;

        public WasmLoadConstantI32Action(MethodAnalysis<WasmInstruction> context, WasmInstruction instruction) : base(context, instruction)
        {
            _theValue = (int) (ulong) instruction.Operands[0];
            
            _constantMade = context.MakeConstant(typeof(int), _theValue);
            context.Stack.Push(_constantMade);
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<WasmInstruction> context, ILProcessor processor)
        {
            throw new NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the literal int32 {_theValue} (0x{_theValue:X}) and pushes it to the stack as new constant {_constantMade.Name}";
        }
    }
}