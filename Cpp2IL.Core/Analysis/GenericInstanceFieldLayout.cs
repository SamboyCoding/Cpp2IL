using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Analysis;

//Resolves field offsets on generic types, which are all 0 in the metadata.
public static class GenericInstanceFieldLayout
{
    public static FieldAnalysisContext? FindFieldAtOffset(TypeAnalysisContext definition, long targetOffset)
    {
        var pointerSize = definition.AppContext.Binary.PointerSizeBytes;
        var offset = 2L * pointerSize;
        return FindField(definition, targetOffset, pointerSize, ref offset, new HashSet<TypeAnalysisContext>(), out _);
    }

    private static FieldAnalysisContext? FindField(TypeAnalysisContext owner, long targetOffset, int pointerSize,
        ref long offset, HashSet<TypeAnalysisContext> visited, out bool knownLayout)
    {
        knownLayout = false;
        var instance = owner as GenericInstanceTypeAnalysisContext;
        var definition = instance?.GenericType ?? owner;
        if (!visited.Add(definition) || instance?.GenericArguments.Any(a => a.IsValueType) == true)
            return null;

        // Constructed types do not populate Fields, and their raw-metadata constructor
        // does not populate BaseType. Walk the definition and substitute its arguments.
        var baseType = definition.BaseType;
        if (baseType != null)
        {
            if (instance != null)
                baseType = GenericInstantiation.Instantiate(baseType, instance.GenericArguments, []);
            var inherited = FindField(baseType, targetOffset, pointerSize, ref offset, visited, out var baseKnown);
            if (inherited != null || !baseKnown)
                return inherited;
            // Tail padding of a base ending in a sub-pointer-sized field is not
            // described here. Do not guess where this type's own fields begin.
            if (offset % pointerSize != 0)
                return null;
        }

        foreach (var declaredField in definition.Fields)
        {
            if (declaredField.IsStatic)
                continue;

            var field = instance == null ? declaredField : new ConcreteGenericFieldAnalysisContext(declaredField, instance);
            if (GetSizeAndAlignment(field.FieldType, pointerSize) is not var (size, alignment))
                return null;

            if (instance != null || definition.GenericParameters.Count > 0)
                offset = (offset + alignment - 1) & ~(alignment - 1);
            else
            {
                // Ordinary ancestors have authoritative absolute offsets. Unknown
                // offsets must not be guessed when locating a later generic field.
                if (field.Offset < offset)
                    return null;
                offset = field.Offset;
            }

            if (offset == targetOffset)
                return field;

            offset += size;
        }

        knownLayout = true;
        return null;
    }

    private static (long Size, long Alignment)? GetSizeAndAlignment(TypeAnalysisContext fieldType, int pointerSize)
    {
        // TODO support user-defined value types
        if (fieldType is GenericParameterTypeAnalysisContext or PointerTypeAnalysisContext || !fieldType.IsValueType)
            return (pointerSize, pointerSize);

        if (fieldType.IsEnumType && fieldType.Fields.FirstOrDefault(f => !f.IsStatic) is { } underlying)
            return GetSizeAndAlignment(underlying.FieldType, pointerSize);

        return fieldType.FullName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => (1, 1),
            "System.Int16" or "System.UInt16" or "System.Char" => (2, 2),
            "System.Int32" or "System.UInt32" or "System.Single" => (4, 4),
            "System.Int64" or "System.UInt64" or "System.Double" => (8, 8),
            "System.IntPtr" or "System.UIntPtr" => (pointerSize, pointerSize),
            _ => null // an arbitrary struct needs its own layout computed, bail rather than guess
        };
    }
}
