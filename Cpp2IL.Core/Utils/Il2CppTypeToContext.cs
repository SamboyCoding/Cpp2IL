using System;
using System.Diagnostics.CodeAnalysis;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils.AsmResolver;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Utils;

internal static class Il2CppTypeToContext
{
    private static TypeAnalysisContext GetPrimitive(this SystemTypesContext context, Il2CppTypeEnum type) => 
        type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_OBJECT => context.SystemObjectType,
            Il2CppTypeEnum.IL2CPP_TYPE_VOID => context.SystemVoidType,
            Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => context.SystemBooleanType,
            Il2CppTypeEnum.IL2CPP_TYPE_CHAR => context.SystemCharType,
            Il2CppTypeEnum.IL2CPP_TYPE_I1 => context.SystemSByteType,
            Il2CppTypeEnum.IL2CPP_TYPE_U1 => context.SystemByteType,
            Il2CppTypeEnum.IL2CPP_TYPE_I2 => context.SystemInt16Type,
            Il2CppTypeEnum.IL2CPP_TYPE_U2 => context.SystemUInt16Type,
            Il2CppTypeEnum.IL2CPP_TYPE_I4 => context.SystemInt32Type,
            Il2CppTypeEnum.IL2CPP_TYPE_U4 => context.SystemUInt32Type,
            Il2CppTypeEnum.IL2CPP_TYPE_I => context.SystemIntPtrType,
            Il2CppTypeEnum.IL2CPP_TYPE_U => context.SystemUIntPtrType,
            Il2CppTypeEnum.IL2CPP_TYPE_I8 => context.SystemInt64Type,
            Il2CppTypeEnum.IL2CPP_TYPE_U8 => context.SystemUInt64Type,
            Il2CppTypeEnum.IL2CPP_TYPE_R4 => context.SystemSingleType,
            Il2CppTypeEnum.IL2CPP_TYPE_R8 => context.SystemDoubleType,
            Il2CppTypeEnum.IL2CPP_TYPE_STRING => context.SystemStringType,
            Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF => context.SystemTypedReferenceType,
            Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX => context.SystemTypeType,
            _ => throw new ArgumentException("Type is not a primitive", nameof(type))
        };
    
    
    [return: NotNullIfNotNull("type")]
    public static TypeAnalysisContext? ResolveIl2CppType(this AssemblyAnalysisContext context, Il2CppType? type)
    {
        if (type == null)
            return null;
        
        if (type.Type.IsIl2CppPrimitive())
            return context.AppContext.SystemTypes.GetPrimitive(type.Type);

        if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
            return context.AppContext.ResolveContextForType(type.AsClass()) ?? throw new($"Could not resolve type context for type {type.AsClass().FullName}");

        if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            return new GenericInstanceTypeAnalysisContext(type, context);

        if (type.Type is Il2CppTypeEnum.IL2CPP_TYPE_BYREF or Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY or Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
            return new WrappedTypeAnalysisContext(type, context);

        return new GenericParameterTypeAnalysisContext(type, context);
    }
}