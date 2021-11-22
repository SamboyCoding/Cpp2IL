using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using WasmDisassembler;

namespace Cpp2IL.Core.Analysis.Actions.WASM
{
    public class WasmCallManagedFunctionAction : AbstractCallAction<WasmInstruction>
    {
        public WasmCallManagedFunctionAction(MethodAnalysis<WasmInstruction> context, WasmInstruction instruction) : base(context, instruction)
        {
            var methodIndex = (int) (ulong) instruction.Operands[0];
            var managedFunctions = WasmUtils.GetMethodDefinitionsAtIndex(methodIndex)!;

            if (managedFunctions.Count == 1)
            {
                ManagedMethodBeingCalled = managedFunctions.Single();

                context.Stack.Pop(); //Pop method info arg

                if (!ManagedMethodBeingCalled.Resolve().IsStatic && context.Stack.Peek() is LocalDefinition)
                    InstanceBeingCalledOn = context.Stack.Pop() as LocalDefinition;
            }
        }
    }
}