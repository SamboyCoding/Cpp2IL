using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using WasmDisassembler;

namespace Cpp2IL.Core.Analysis.Actions.WASM
{
    public class WasmReturnAction : AbstractReturnAction<WasmInstruction>
    {
        public WasmReturnAction(MethodAnalysis<WasmInstruction> context, WasmInstruction instruction) : base(context, instruction)
        {
            if(_isVoid || context.Stack.Count == 0)
                return;

            returnValue = context.Stack.Pop();
            
            if (returnValue is LocalDefinition l)
                RegisterUsedLocal(l, context);
            
            TryCorrectConstant(context);
        }
    }
}