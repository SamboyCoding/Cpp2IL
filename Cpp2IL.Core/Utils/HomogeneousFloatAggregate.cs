using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Utils;

/// <summary>
/// AAPCS64 passes a "homogeneous floating-point aggregate" - a struct whose instance fields are all
/// <c>float</c> or all <c>double</c>, one to four of them - in consecutive SIMD registers, one per
/// element, instead of in a single integer register. IL2CPP follows the platform ABI for managed
/// methods, so <c>Vector3</c> occupies V0..V2 and the following <c>float</c> argument goes in V3.
///
/// Without this the argument slots are counted wrong for every call that takes such a struct: the
/// struct eats one integer register it does not use, and every argument after it is read from the
/// wrong register - typically the null <c>MethodInfo</c> slot, which is where literal zeros in call
/// arguments come from.
/// </summary>
public static class HomogeneousFloatAggregate
{
    private const int MaxElements = 4;   // four SIMD registers at most
    private const int MaxDepth = 4;      // nested aggregates are flattened; guard against cycles

    private enum ElementKind
    {
        Unknown,
        Single,
        Double
    }

    /// <summary>
    /// Number of SIMD registers the type occupies when passed by value, or 0 when it is not a
    /// homogeneous float aggregate (or its layout is not knowable, e.g. an open generic).
    /// </summary>
    public static int Count(TypeAnalysisContext type)
    {
        if (!type.IsValueType)
            return 0;

        var kind = ElementKind.Unknown;
        var total = 0;

        if (!TryFlatten(type, ref kind, ref total, 0))
            return 0;

        return total is > 0 and <= MaxElements ? total : 0;
    }

    private static bool TryFlatten(TypeAnalysisContext type, ref ElementKind kind, ref int total, int depth)
    {
        if (depth > MaxDepth)
            return false;

        if (BaseCallingConventionResolver.IsFloatingPoint(type))
        {
            var element = type == type.AppContext.SystemTypes.SystemDoubleType ? ElementKind.Double : ElementKind.Single;

            if (kind == ElementKind.Unknown)
                kind = element;
            else if (kind != element)
                return false; // mixing float and double is not homogeneous

            total++;
            return true;
        }

        if (!type.IsValueType)
            return false;

        var fields = InstanceFields(type);
        if (fields.Count == 0)
            return false; // no definition / no fields: layout not knowable, stay conservative

        foreach (var field in fields)
            if (!TryFlatten(field.FieldType, ref kind, ref total, depth + 1))
                return false;

        return true;
    }

    private static List<FieldAnalysisContext> InstanceFields(TypeAnalysisContext type)
    {
        var fields = new List<FieldAnalysisContext>();

        foreach (var field in type.Fields)
        {
            if (field.IsStatic)
                continue;

            // A field type that cannot be resolved means the layout is not trustworthy here.
            if (field.FieldType == null)
                return [];

            fields.Add(field);
        }

        return fields;
    }
}
