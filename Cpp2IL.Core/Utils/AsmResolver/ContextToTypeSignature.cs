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

    public static TypeSignature ToTypeSignature(this TypeAnalysisContext context, ModuleDefinition parentModule) => context switch
    {
        ReferencedTypeAnalysisContext referencedTypeAnalysisContext => referencedTypeAnalysisContext.ToTypeSignature(parentModule),
        _ => parentModule.DefaultImporter.ImportType(context.GetTypeDefinition()).ToTypeSignature(parentModule.RuntimeContext)
    };

    public static TypeSignature ToTypeSignature(this ReferencedTypeAnalysisContext context, ModuleDefinition parentModule) => context switch
    {
        GenericParameterTypeAnalysisContext genericParameterTypeAnalysisContext => genericParameterTypeAnalysisContext.ToTypeSignature(parentModule),
        GenericInstanceTypeAnalysisContext genericInstanceTypeAnalysisContext => genericInstanceTypeAnalysisContext.ToTypeSignature(parentModule),
        WrappedTypeAnalysisContext wrappedTypeAnalysisContext => wrappedTypeAnalysisContext.ToTypeSignature(parentModule),
        SentinelTypeAnalysisContext => SentinelTypeSignature.Instance,
        _ => throw new ArgumentException($"Unknown referenced type context {context.GetType()}", nameof(context))
    };

    public static GenericInstanceTypeSignature ToTypeSignature(this GenericInstanceTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        var genericType = context.GenericType.ToTypeSignature(parentModule).ToTypeDefOrRef();
        var genericArguments = context.GenericArguments.Select(a => a.ToTypeSignature(parentModule)).ToArray();

        return new GenericInstanceTypeSignature(genericType, context.IsValueType, genericArguments);
    }

    public static GenericParameterSignature ToTypeSignature(this GenericParameterTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return new GenericParameterSignature(parentModule, context.Type == Il2CppTypeEnum.IL2CPP_TYPE_VAR ? GenericParameterType.Type : GenericParameterType.Method, context.Index);
    }

    public static TypeSpecificationSignature ToTypeSignature(this WrappedTypeAnalysisContext context, ModuleDefinition parentModule) => context switch
    {
        SzArrayTypeAnalysisContext szArrayTypeAnalysisContext => szArrayTypeAnalysisContext.ToTypeSignature(parentModule),
        PointerTypeAnalysisContext pointerTypeAnalysisContext => pointerTypeAnalysisContext.ToTypeSignature(parentModule),
        ByRefTypeAnalysisContext byReferenceTypeAnalysisContext => byReferenceTypeAnalysisContext.ToTypeSignature(parentModule),
        ArrayTypeAnalysisContext arrayTypeAnalysisContext => arrayTypeAnalysisContext.ToTypeSignature(parentModule),
        PinnedTypeAnalysisContext pinnedTypeAnalysisContext => pinnedTypeAnalysisContext.ToTypeSignature(parentModule),
        BoxedTypeAnalysisContext boxedTypeAnalysisContext => boxedTypeAnalysisContext.ToTypeSignature(parentModule),
        CustomModifierTypeAnalysisContext customModifierTypeAnalysisContext => customModifierTypeAnalysisContext.ToTypeSignature(parentModule),
        _ => throw new ArgumentException($"Unknown wrapped type context {context.GetType()}", nameof(context))
    };

    public static SzArrayTypeSignature ToTypeSignature(this SzArrayTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakeSzArrayType();
    }

    public static PointerTypeSignature ToTypeSignature(this PointerTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakePointerType();
    }

    public static ByReferenceTypeSignature ToTypeSignature(this ByRefTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakeByReferenceType();
    }

    public static ArrayTypeSignature ToTypeSignature(this ArrayTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakeArrayTypeWithLowerBounds(context.Rank);
    }

    public static PinnedTypeSignature ToTypeSignature(this PinnedTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakePinnedType();
    }

    public static BoxedTypeSignature ToTypeSignature(this BoxedTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakeBoxedType();
    }

    public static CustomModifierTypeSignature ToTypeSignature(this CustomModifierTypeAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ElementType.ToTypeSignature(parentModule).MakeModifierType(context.ModifierType.ToTypeSignature(parentModule).ToTypeDefOrRef(), context.Required);
    }

    public static TypeSignature ToTypeSignature(this ParameterAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.ParameterType.ToTypeSignature(parentModule);
    }

    public static TypeSignature ToTypeSignature(this FieldAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.FieldType.ToTypeSignature(parentModule);
    }

    public static TypeSignature ToTypeSignature(this EventAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.EventType.ToTypeSignature(parentModule);
    }

    public static TypeSignature ToTypeSignature(this PropertyAnalysisContext context, ModuleDefinition parentModule)
    {
        return context.PropertyType.ToTypeSignature(parentModule);
    }
}
