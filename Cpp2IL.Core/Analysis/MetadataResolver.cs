using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.Analysis;

public static class MetadataResolver
{
    public static void ResolveAll(MethodAnalysisContext method)
    {
        ResolveCalls(method);
        ResolveFieldOffsets(method);
        ResolveGetter(method);
        ResolveStrings(method);
    }

    private static void ResolveStrings(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode != OpCode.Move)
                continue;

            if ((instruction.Operands[0] is not LocalVariable) || (instruction.Operands[1] is not MemoryOperand memory))
                continue;

            if (memory.Base == null && memory.Index == null && memory.Scale == 0)
            {
                var stringLiteral = LibCpp2IlMain.GetLiteralByAddress((ulong)memory.Addend);

                if (stringLiteral == null)
                {
                    // Try instead check if its type metadata usage
                    var metadataUsage = LibCpp2IlMain.GetTypeGlobalByAddress((ulong)memory.Addend);
                    if (metadataUsage != null && method.DeclaringType is not null)
                    {
                        var typeAnalysisContext = metadataUsage.ToContext(method.DeclaringType!.DeclaringAssembly);
                        if (typeAnalysisContext != null)
                            instruction.Operands[1] = typeAnalysisContext;
                    }

                    continue;
                }

                instruction.Operands[1] = stringLiteral;
            }
        }
    }

    private static void ResolveFieldOffsets(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is not MemoryOperand memory)
                    continue;

                // Has to be [base (local) + addend (field offset)]
                if (memory.Index != null || memory.Scale != 0)
                    continue;

                if (memory.Base is not LocalVariable local || local?.Type == null)
                    continue;

                var field = local.Type.Fields.FirstOrDefault(f => f.BackingData?.FieldOffset == memory.Addend);

                if (field == null) // TODO: Support nested fields (Field1.Field2.Field3)
                    continue;

                instruction.Operands[i] = new FieldReference(field, local, (int)memory.Addend);
            }
        }
    }

    private static void ResolveCalls(MethodAnalysisContext method)
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

    private static void HandleKeyFunction(ApplicationAnalysisContext appContext, Instruction instruction, ulong target, BaseKeyFunctionAddresses kFA)
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

    // Because of il2cpp fields (like cctor_finished_or_no_cctor) [local @ reg+offset] sometimes can't be resolved, but this works for now
    private static void ResolveGetter(MethodAnalysisContext method)
    {
        if (!method.Name.StartsWith("get_"))
            return;

        // Default get: Return [this @ reg+offset]
        var instructions = method.ControlFlowGraph!.Instructions;
        if (instructions.Count == 1)
        {
            var instr = instructions[0];

            if (instr.OpCode != OpCode.Return
                || instr.Operands.Count < 1
                || instr.Operands[0] is not MemoryOperand memory
                || memory.Index != null || memory.Scale != 0
                || memory.Base is not LocalVariable local)
                return;

            var fieldName = $"<{method.Name[4..]}>k__BackingField";

            var field = method.DeclaringType!.Fields.Find(f => f.Name == fieldName);
            if (field == null)
                return;

            instr.Operands[0] = new FieldReference(field, local, (int)memory.Addend);
        }
    }
}
