using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Graphs.Processors;

internal class CallProcessor : IBlockProcessor
{
    public void Process(MethodAnalysisContext methodAnalysisContext, Block block)
    {
        if (block.BlockType != BlockType.Call)
            return;
        var callInstruction = block.Instructions[^1];
        if (callInstruction == null)
            return;
        if (!callInstruction.IsCall)
            return;

        if (callInstruction.Operands.Count <= 0)
            return;
        var dest = callInstruction.Operands[0];
        if (!dest.IsNumeric())
            return;

        var target = (ulong)dest;

        var keyFunctionAddresses = methodAnalysisContext.AppContext.GetOrCreateKeyFunctionAddresses();

        if (keyFunctionAddresses.IsKeyFunctionAddress(target))
        {
            HandleKeyFunction(methodAnalysisContext.AppContext, callInstruction, target, keyFunctionAddresses);
            return;
        }

        //Non-key function call. Try to find a single match
        if (!methodAnalysisContext.AppContext.MethodsByAddress.TryGetValue(target, out var targetMethods))
            return;

        if (targetMethods is not [{ } singleTargetMethod])
            return;

        callInstruction.Operands[0] = singleTargetMethod;
    }

    private void HandleKeyFunction(ApplicationAnalysisContext appContext, Instruction instruction, ulong target, BaseKeyFunctionAddresses kFA)
    {
        // TODO: Handle labelling functions calls that match these in a more graceful manner
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
            var index = pairs.FindIndex(pair => pair.Value == target);
            method = pairs[index].Key;
        }

        if (method != "")
        {
            instruction.Operands[0] = method;
        }
    }
}
