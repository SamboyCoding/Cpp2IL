using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Follows the runtime generic context chain and maps to actual values.
/// </summary>
public static class RgctxResolver
{
    public static bool Run(MethodAnalysisContext method)
    {
        var is32Bit = method.AppContext.Binary.is32Bit;
        var klassOffset = is32Bit ? 0x10 : 0x20;
        var rgctxOffset = is32Bit ? 0x60 : 0xC0;
        var pointerSize = is32Bit ? 4 : 8;

        var changed = false;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode != OpCode.Move || instruction.Operands.Count < 2)
                continue;

            if (instruction.Operands[0] is not LocalVariable destination)
                continue;

            if (instruction.Operands[1] is not MemoryOperand { Index: null, Scale: 0, Base: LocalVariable source } memory)
                continue;

            var resolved = source.Type switch
            {
                // MethodInfo::klass - the instance the method belongs to, which is what carries the RGCTX
                RuntimeMethodInfoAnalysisContext info when memory.Addend == klassOffset && info.RepresentedMethod.DeclaringType is { } declaring
                    => new RuntimeClassTypeAnalysisContext(declaring, declaring.DeclaringAssembly),

                RuntimeClassTypeAnalysisContext { RepresentedType: var owner } when memory.Addend == rgctxOffset
                    => new RgctxTableTypeAnalysisContext(owner, owner.DeclaringAssembly),

                RgctxTableTypeAnalysisContext { OwnerType: var instance } when memory.Addend % pointerSize == 0
                    => ResolveEntry(instance, (int)(memory.Addend / pointerSize)),

                _ => null,
            };

            // Wrappers are not unique objects, so compare what they contain, not references
            // TODO maybe fix this? It's allocation spam if nothing else. Just make a canonical RuntimeClassTypeAnalysisContext/RgctxTableTypeAnalysisContext
            if (resolved == null || DescribesSameThing(destination.Type, resolved))
                continue;

            destination.Type = resolved;
            changed = true;
        }

        return changed;
    }

    private static bool DescribesSameThing(TypeAnalysisContext? existing, TypeAnalysisContext candidate) =>
        (existing, candidate) switch
        {
            (RuntimeClassTypeAnalysisContext a, RuntimeClassTypeAnalysisContext b) => ReferenceEquals(a.RepresentedType, b.RepresentedType),
            (RgctxTableTypeAnalysisContext a, RgctxTableTypeAnalysisContext b) => ReferenceEquals(a.OwnerType, b.OwnerType),
            _ => ReferenceEquals(existing, candidate),
        };

    private static TypeAnalysisContext? ResolveEntry(TypeAnalysisContext instance, int index)
    {
        var definition = (instance as GenericInstanceTypeAnalysisContext)?.GenericType ?? instance;

        if (definition.Definition is not { } typeDefinition)
            return null;

        var entries = typeDefinition.RgctXs;

        if (index < 0 || index >= entries.Length)
            return null;

        var entry = entries[index];

        if (entry.type is not (Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS or Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE))
            return null;

        var entryType = instance.AppContext.ResolveIl2CppType(entry.Type);

        if (Inflate(entryType, instance) is not { } inflated)
            return null;

        return new RuntimeClassTypeAnalysisContext(inflated, inflated.DeclaringAssembly);
    }

    private static TypeAnalysisContext? Inflate(TypeAnalysisContext? type, TypeAnalysisContext instance)
    {
        if (instance is not GenericInstanceTypeAnalysisContext { GenericArguments: var arguments })
            return type;

        switch (type)
        {
            case GenericParameterTypeAnalysisContext { Index: var i }:
                return i >= 0 && i < arguments.Count ? arguments[i] : null;

            case GenericInstanceTypeAnalysisContext nested:
                // Self-reference inflates to the instance we already have.
                return nested.GenericArguments.All(a => a is GenericParameterTypeAnalysisContext)
                    ? instance
                    : null;

            default:
                return type;
        }
    }
}
