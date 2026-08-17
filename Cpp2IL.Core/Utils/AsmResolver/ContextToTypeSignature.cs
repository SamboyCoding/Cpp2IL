using System;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Utils.AsmResolver;

public static class ContextToTypeSignature
{
    private static TypeDefinition GetTypeDefinition(this TypeAnalysisContext context)
    {
        return context.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {context.FullName}");
    }

    public static TypeSignature ToTypeSignature(this TypeAnalysisContext context) => context switch
    {
        ReferencedTypeAnalysisContext referencedTypeAnalysisContext => referencedTypeAnalysisContext.ToTypeSignature(),
        _ => context.GetTypeDefinition().ToTypeSignature(context.IsValueType)
    };

    public static TypeSignature ToTypeSignature(this ReferencedTypeAnalysisContext context) => context switch
    {
        GenericParameterTypeAnalysisContext genericParameterTypeAnalysisContext => genericParameterTypeAnalysisContext.ToTypeSignature(),
        GenericInstanceTypeAnalysisContext genericInstanceTypeAnalysisContext => genericInstanceTypeAnalysisContext.ToTypeSignature(),
        WrappedTypeAnalysisContext wrappedTypeAnalysisContext => wrappedTypeAnalysisContext.ToTypeSignature(),
        SentinelTypeAnalysisContext => SentinelTypeSignature.Instance,
        // An Il2CppClass*/MethodInfo*/FieldInfo*/static storage runtime handle has no managed type; lower it to a raw pointer-sized value.
        RuntimeClassTypeAnalysisContext or RuntimeMethodInfoAnalysisContext or RuntimeFieldInfoAnalysisContext
            or StaticFieldStorageTypeAnalysisContext or RgctxTableTypeAnalysisContext or MethodRgctxTableTypeAnalysisContext
            => context.AppContext.SystemTypes.SystemIntPtrType.ToTypeSignature(),
        _ => throw new ArgumentException($"Unknown referenced type context {context.GetType()}", nameof(context))
    };

    public static GenericInstanceTypeSignature ToTypeSignature(this GenericInstanceTypeAnalysisContext context)
    {
        var genericType = context.GenericType.ToTypeSignature().ToTypeDefOrRef();
        var genericArguments = context.GenericArguments.Select(a => a.ToTypeSignature());

        return new GenericInstanceTypeSignature(genericType, context.IsValueType, genericArguments);
    }

    public static GenericParameterSignature ToTypeSignature(this GenericParameterTypeAnalysisContext context)
    {
        return new GenericParameterSignature(context.Type == Il2CppTypeEnum.IL2CPP_TYPE_VAR ? GenericParameterType.Type : GenericParameterType.Method, context.Index);
    }

    public static TypeSpecificationSignature ToTypeSignature(this WrappedTypeAnalysisContext context) => context switch
    {
        SzArrayTypeAnalysisContext szArrayTypeAnalysisContext => szArrayTypeAnalysisContext.ToTypeSignature(),
        PointerTypeAnalysisContext pointerTypeAnalysisContext => pointerTypeAnalysisContext.ToTypeSignature(),
        ByRefTypeAnalysisContext byReferenceTypeAnalysisContext => byReferenceTypeAnalysisContext.ToTypeSignature(),
        ArrayTypeAnalysisContext arrayTypeAnalysisContext => arrayTypeAnalysisContext.ToTypeSignature(),
        PinnedTypeAnalysisContext pinnedTypeAnalysisContext => pinnedTypeAnalysisContext.ToTypeSignature(),
        BoxedTypeAnalysisContext boxedTypeAnalysisContext => boxedTypeAnalysisContext.ToTypeSignature(),
        CustomModifierTypeAnalysisContext customModifierTypeAnalysisContext => customModifierTypeAnalysisContext.ToTypeSignature(),
        _ => throw new ArgumentException($"Unknown wrapped type context {context.GetType()}", nameof(context))
    };

    public static SzArrayTypeSignature ToTypeSignature(this SzArrayTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakeSzArrayType();
    }

    public static PointerTypeSignature ToTypeSignature(this PointerTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakePointerType();
    }

    public static ByReferenceTypeSignature ToTypeSignature(this ByRefTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakeByReferenceType();
    }

    public static ArrayTypeSignature ToTypeSignature(this ArrayTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakeArrayTypeWithLowerBounds(context.Rank);
    }

    public static PinnedTypeSignature ToTypeSignature(this PinnedTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakePinnedType();
    }

    public static BoxedTypeSignature ToTypeSignature(this BoxedTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakeBoxedType();
    }

    public static CustomModifierTypeSignature ToTypeSignature(this CustomModifierTypeAnalysisContext context)
    {
        return context.ElementType.ToTypeSignature().MakeModifierType(context.ModifierType.ToTypeSignature().ToTypeDefOrRef(), context.Required);
    }

    public static TypeSignature ToTypeSignature(this ParameterAnalysisContext context)
    {
        return context.ParameterType.ToTypeSignature();
    }

    public static TypeSignature ToTypeSignature(this FieldAnalysisContext context)
    {
        return context.FieldType.ToTypeSignature();
    }

    public static TypeSignature ToTypeSignature(this EventAnalysisContext context)
    {
        return context.EventType.ToTypeSignature();
    }

    public static TypeSignature ToTypeSignature(this PropertyAnalysisContext context)
    {
        return context.PropertyType.ToTypeSignature();
    }
}
