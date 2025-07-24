using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public class ResolveCalls : IAction
{
    public void Apply(MethodAnalysisContext method)
    {
        foreach (var block in method.ControlFlowGraph!.Blocks)
        {
            if (block.BlockType != BlockType.Call && block.BlockType != BlockType.TailCall)
                continue;

            var callInstruction = block.Instructions[^1];
            var dest = callInstruction.Operands[0];

            if (!dest.IsNumeric())
                continue;

            var target = (ulong)dest;

            var keyFunctionAddresses = method.AppContext.GetOrCreateKeyFunctionAddresses();

            if (keyFunctionAddresses.IsKeyFunctionAddress(target))
            {
                HandleKeyFunction(method.AppContext, callInstruction, target, keyFunctionAddresses);
                continue;
            }

            //Non-key function call. Try to find a single match
            if (!method.AppContext.MethodsByAddress.TryGetValue(target, out var targetMethods))
                continue;

            if (targetMethods is not [{ } singleTargetMethod])
                continue;

            callInstruction.Operands[0] = singleTargetMethod;
        }

        method.ControlFlowGraph.MergeCallBlocks();
    }

    private void HandleKeyFunction(ApplicationAnalysisContext appContext, Instruction instruction, ulong target, BaseKeyFunctionAddresses kFA)
    {
        var method = "";
        if (target == kFA.il2cpp_codegen_initialize_method || target == kFA.il2cpp_codegen_initialize_runtime_metadata)
        {
            if (appContext.MetadataVersion < 27)
            {
                method = nameof(kFA.il2cpp_codegen_initialize_method);
            }
            else
            {
                method = nameof(kFA.il2cpp_codegen_initialize_runtime_metadata);
            }
        }
        else
        {
            var pairs = kFA.Pairs.ToList();
            var key = pairs.FirstOrDefault(pair => pair.Value == target).Key;
            if (key == null)
                return;
            method = key;
        }

        if (method != "")
        {
            instruction.Operands[0] = method;
        }
    }
}
