using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

//Resolves field offsets on generic types, which are all 0 in the metadata.
public static class GenericInstanceFieldLayout
{
    public static FieldAnalysisContext? FindFieldAtOffset(
        TypeAnalysisContext definition,
        long targetOffset,
        IReadOnlyList<TypeAnalysisContext>? genericArguments = null)
    {
        var pointerSize = definition.AppContext.Binary.PointerSizeBytes;

        // Base type offset calculation:
        // Value types have no object header (start at 0).
        // Reference types have an object header (klass + monitor pointer: 2 * pointerSize).
        var startOffset = definition.IsValueType ? 0L : 2L * pointerSize;

        // Compute offset contributed by base types
        var baseTypeOffset = ComputeBaseTypeOffset(definition.BaseType, pointerSize, startOffset);
        if (baseTypeOffset == null)
            return null;

        var currentOffset = baseTypeOffset.Value;
        var nonStaticFields = definition.Fields.Where(f => !f.IsStatic).ToList();
        if (nonStaticFields.Count == 0)
            return null;

        var exactCalculationSuccessful = true;
        var hasUnresolvedGenericParam = false;

        foreach (var field in nonStaticFields)
        {
            var actualFieldType = ResolveFieldType(field.FieldType, genericArguments);
            if (actualFieldType is GenericParameterTypeAnalysisContext)
                hasUnresolvedGenericParam = true;

            var sizeAndAlign = GetSizeAndAlignment(actualFieldType, pointerSize, genericArguments);
            if (sizeAndAlign is not var (size, alignment))
            {
                exactCalculationSuccessful = false;
                break;
            }

            currentOffset = (currentOffset + alignment - 1) & ~(alignment - 1);

            if (currentOffset == targetOffset)
                return field;

            currentOffset += size;
        }

        if (exactCalculationSuccessful && !hasUnresolvedGenericParam)
            return null;

        // Heuristic fallback for open generic types or types with unmeasured struct fields
        return FallbackFindField(nonStaticFields, targetOffset, baseTypeOffset.Value, pointerSize);
    }

    private static long? ComputeBaseTypeOffset(TypeAnalysisContext? baseType, int pointerSize, long defaultStart)
    {
        if (baseType == null || baseType.FullName == "System.Object" || baseType.FullName == "System.ValueType")
            return defaultStart;

        var current = baseType;
        var maxFieldEnd = defaultStart;

        while (current != null && current.FullName != "System.Object" && current.FullName != "System.ValueType")
        {
            var baseFields = current.Fields.Where(f => !f.IsStatic).ToList();
            foreach (var f in baseFields)
            {
                if (f.BackingData?.FieldOffset is { } fo && fo >= 0)
                {
                    var fSize = GetSizeAndAlignment(f.FieldType, pointerSize, null)?.Size ?? pointerSize;
                    maxFieldEnd = Math.Max(maxFieldEnd, fo + fSize);
                }
            }
            current = current.BaseType;
        }

        return maxFieldEnd;
    }

    private static TypeAnalysisContext ResolveFieldType(
        TypeAnalysisContext fieldType,
        IReadOnlyList<TypeAnalysisContext>? genericArguments)
    {
        if (genericArguments != null && fieldType is GenericParameterTypeAnalysisContext genericParam)
        {
            var idx = genericParam.Index;
            if (idx >= 0 && idx < genericArguments.Count)
                return genericArguments[idx];
        }
        return fieldType;
    }

    private static (long Size, long Alignment)? GetSizeAndAlignment(
        TypeAnalysisContext fieldType,
        int pointerSize,
        IReadOnlyList<TypeAnalysisContext>? genericArguments)
    {
        if (!fieldType.IsValueType || fieldType is PointerTypeAnalysisContext)
            return (pointerSize, pointerSize);

        if (fieldType is GenericParameterTypeAnalysisContext)
            return (pointerSize, pointerSize);

        if (fieldType.IsEnumType)
        {
            var underlying = fieldType.Fields.FirstOrDefault(f => !f.IsStatic)?.FieldType
                ?? fieldType.DefaultEnumUnderlyingType;
            if (underlying != null && underlying != fieldType)
                return GetSizeAndAlignment(underlying, pointerSize, genericArguments);
            return (4, 4);
        }

        return fieldType.FullName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => (1, 1),
            "System.Int16" or "System.UInt16" or "System.Char" => (2, 2),
            "System.Int32" or "System.UInt32" or "System.Single" => (4, 4),
            "System.Int64" or "System.UInt64" or "System.Double" => (8, 8),
            "System.IntPtr" or "System.UIntPtr" => (pointerSize, pointerSize),
            "System.Decimal" => (16, 8),
            _ => ComputeStructSizeAndAlignment(fieldType, pointerSize, genericArguments)
        };
    }

    private static (long Size, long Alignment)? ComputeStructSizeAndAlignment(
        TypeAnalysisContext structType,
        int pointerSize,
        IReadOnlyList<TypeAnalysisContext>? genericArguments)
    {
        var fields = structType.Fields.Where(f => !f.IsStatic).ToList();
        if (fields.Count == 0)
            return (1, 1);

        long maxAlign = 1;
        long currentOffset = 0;
        foreach (var field in fields)
        {
            var fieldType = ResolveFieldType(field.FieldType, genericArguments);
            if (fieldType == structType)
                return null; // Avoid cycle

            var fieldSizeAlign = GetSizeAndAlignment(fieldType, pointerSize, genericArguments);
            if (fieldSizeAlign is not var (fSize, fAlign))
                return null;

            maxAlign = Math.Max(maxAlign, fAlign);
            currentOffset = (currentOffset + fAlign - 1) & ~(fAlign - 1);
            currentOffset += fSize;
        }

        var structSize = (currentOffset + maxAlign - 1) & ~(maxAlign - 1);
        return (Math.Max(structSize, 1), maxAlign);
    }

    private static FieldAnalysisContext? FallbackFindField(
        List<FieldAnalysisContext> fields,
        long targetOffset,
        long startOffset,
        int pointerSize)
    {
        if (fields.Count == 0 || targetOffset < startOffset)
            return null;

        if (targetOffset == startOffset)
            return fields[0];

        if (fields.Count == 2)
        {
            if (targetOffset <= startOffset + 2 * pointerSize)
                return fields[1];
        }

        long minOffset = startOffset;
        long maxOffset = startOffset;

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (i == 0)
            {
                if (targetOffset == startOffset)
                    return field;
                minOffset = startOffset + 1;
                maxOffset = startOffset + pointerSize;
                continue;
            }

            if (targetOffset >= minOffset && targetOffset <= maxOffset + (pointerSize - 1))
                return field;

            minOffset += 1;
            maxOffset = (maxOffset + pointerSize + pointerSize - 1) & ~(pointerSize - 1);
        }

        return null;
    }
}
