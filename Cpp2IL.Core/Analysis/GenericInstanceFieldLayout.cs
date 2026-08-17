using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

//Resolves field offsets on generic types, which are all 0 in the metadata.
public static class GenericInstanceFieldLayout
{
    public static FieldAnalysisContext? FindFieldAtOffset(TypeAnalysisContext definition, long targetOffset)
    {
        var pointerSize = definition.AppContext.Binary.PointerSizeBytes;

        // TODO Support anything outside the trivial case.
        for (var baseType = definition.BaseType; baseType != null; baseType = baseType.BaseType)
            if (baseType.Fields.Any(f => !f.IsStatic))
                return null;

        var offset = 2L * pointerSize;

        foreach (var field in definition.Fields)
        {
            if (field.IsStatic)
                continue;

            if (GetSizeAndAlignment(field.FieldType, pointerSize) is not var (size, alignment))
                return null;

            offset = (offset + alignment - 1) & ~(alignment - 1);

            if (offset == targetOffset)
                return field;

            offset += size;
        }

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
