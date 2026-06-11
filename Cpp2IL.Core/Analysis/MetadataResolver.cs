using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Analysis;

public static class MetadataResolver
{
    public static void ResolveAll(MethodAnalysisContext method)
    {
        ResolveCalls(method);
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
                var stringLiteral = method.AppContext.LibCpp2IlContext.GetLiteralByAddress((ulong)memory.Addend);

                if (stringLiteral == null)
                {
                    // Try instead check if its type metadata usage
                    var metadataUsage = method.AppContext.LibCpp2IlContext.GetTypeGlobalByAddress((ulong)memory.Addend);
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

    /// <summary>
    /// Replaces every <c>[base + addend]</c> memory operand whose base is a typed local with a
    /// <see cref="FieldReference"/> to the field at that offset. Returns whether any operand was
    /// resolved this pass, so the type/field fixpoint can detect convergence: as more bases become
    /// typed (a field load types its result, which is the base of the next load), more offsets
    /// resolve, so this is re-run until it stops finding new fields.
    /// </summary>
    public static bool ResolveFieldOffsets(MethodAnalysisContext method)
    {
        var changed = false;

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
                changed = true;
            }
        }

        return changed;
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

            // Several methods can share one address (identical native code merged by the linker, or
            // generic sharing). Those are left as a numeric target here and disambiguated later by
            // receiver type in ResolveAmbiguousCalls, once types are known.
            if (targetMethods is not [{ } singleTargetMethod])
                continue;

            callInstruction.Operands[0] = singleTargetMethod;
        }

        method.ControlFlowGraph.MergeCallBlocks();
    }

    /// <summary>
    /// Resolves calls whose address maps to more than one method by matching the receiver's known
    /// type against the candidates' declaring types. Runs inside the type/field fixpoint and so
    /// re-fires as receivers become typed - a resolved call types its return value, which can type
    /// the receiver of a further call. Returns whether any call was resolved this pass.
    ///
    /// Conservative by design: it commits only when exactly one non-static candidate's declaring
    /// type matches the receiver's type. Anything still untyped or ambiguous is left for a later
    /// pass, or left unresolved - it never guesses.
    /// </summary>
    public static bool ResolveAmbiguousCalls(MethodAnalysisContext method)
    {
        var changed = false;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            var target = instruction.Operands[0];

            // A resolved call's target is a method/key-function name; only unresolved ones are still numeric.
            if (!target.IsNumeric())
                continue;

            if (!method.AppContext.MethodsByAddress.TryGetValue((ulong)target, out var candidates) || candidates.Count < 2)
                continue;

            if (GetReceiver(instruction) is not { Type: { } receiverType })
                continue;

            MethodAnalysisContext? match = null;
            var ambiguous = false;

            foreach (var candidate in candidates)
            {
                if (candidate.IsStatic || !ReferenceEquals(candidate.DeclaringType, receiverType))
                    continue;

                if (match != null)
                {
                    ambiguous = true;
                    break;
                }

                match = candidate;
            }

            if (ambiguous || match == null)
                continue;

            instruction.Operands[0] = match;
            changed = true;
        }

        return changed;
    }

    // The receiver ('this') of a call is the first integer-slot argument: operand 1 for CallVoid
    // (after the target), operand 2 for Call (after the target and the return value).
    private static LocalVariable? GetReceiver(Instruction call)
    {
        var index = call.OpCode == OpCode.CallVoid ? 1 : 2;
        return index < call.Operands.Count ? call.Operands[index] as LocalVariable : null;
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
