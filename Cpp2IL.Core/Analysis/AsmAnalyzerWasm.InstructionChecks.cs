using Cpp2IL.Core.Analysis.Actions.WASM;
using Cpp2IL.Core.Utils;
using WasmDisassembler;

namespace Cpp2IL.Core.Analysis
{
    public partial class AsmAnalyzerWasm
    {
        private void AnalyzeZeroOperandInstruction(WasmInstruction instruction)
        {
            switch (instruction.Mnemonic)
            {
                case WasmMnemonic.End when instruction.NextIp >= Analysis.AbsoluteMethodEnd:
                    Analysis.Actions.Add(new WasmReturnAction(Analysis, instruction));
                    break;
            }
        }

        private void AnalyzeOneOperandInstruction(WasmInstruction instruction)
        {
            switch (instruction.Mnemonic)
            {
                case WasmMnemonic.I32Const:
                    Analysis.Actions.Add(new WasmLoadConstantI32Action(Analysis, instruction));
                    break;
                case WasmMnemonic.Call:
                    var methodIndex = (int) (ulong) instruction.Operands[0];
                    if(WasmUtils.GetMethodDefinitionsAtIndex(methodIndex) is {}) 
                        Analysis.Actions.Add(new WasmCallManagedFunctionAction(Analysis, instruction));
                    break;
            }
        }

        protected override void PerformInstructionChecks(WasmInstruction instruction)
        {
            switch (instruction.Operands.Length)
            {
                case 0:
                    AnalyzeZeroOperandInstruction(instruction);
                    break;
                case 1:
                    AnalyzeOneOperandInstruction(instruction);
                    break;
                case 2:
                    break;
            }
        }
    }
}