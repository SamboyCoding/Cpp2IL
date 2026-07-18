using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Utils;

public static class Il2CppTypeToContext
{
    [return: NotNullIfNotNull(nameof(type))]
    public static TypeAnalysisContext? ResolveIl2CppType(this ApplicationAnalysisContext context, Il2CppType? type)
    {
        if (type == null)
            return null;

        TypeAnalysisContext ret;

        Debug.Assert(type.Type is not Il2CppTypeEnum.IL2CPP_TYPE_BYREF, $"ByRef types should be indicated separately with the {nameof(type.Byref)} flag");

        if (type.Type.IsIl2CppPrimitive())
            ret = context.ResolveContextForType(context.LibCpp2IlContext.ReflectionCache.PrimitiveTypeDefinitions[type.Type]) ?? throw new($"Could not resolve type context for type {type.Type}");
        else if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
            ret = context.ResolveContextForType(type.AsClass()) ?? throw new($"Could not resolve type context for type {type.AsClass().FullName}");
        else if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            ret = GenericInstanceTypeAnalysisContext.GetOrCreate(type, context);
        else if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY or Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
            ret = WrappedTypeAnalysisContext.Create(type, context);
        else
            ret = context.ResolveContextForGenericParameter(type.GetGenericParameterDef()) ?? throw new($"Could not resolve type context for type {type.GetGenericParameterDef().Name}");

        if (type.Byref == 1)
            //Byref types need to be wrapped in a byref context so that we don't have incorrect method signatures.
            ret = ret.MakeByReferenceType();

        return ret;
    }
}
