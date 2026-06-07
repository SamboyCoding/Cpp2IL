using System;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class SystemTypesContext
{
    private readonly ApplicationAnalysisContext _appContext;

    public TypeAnalysisContext SystemObjectType { get; }
    public TypeAnalysisContext SystemVoidType { get; }
    public TypeAnalysisContext SystemBooleanType { get; }
    public TypeAnalysisContext SystemCharType { get; }
    public TypeAnalysisContext SystemSByteType { get; }
    public TypeAnalysisContext SystemByteType { get; }
    public TypeAnalysisContext SystemInt16Type { get; }
    public TypeAnalysisContext SystemUInt16Type { get; }
    public TypeAnalysisContext SystemInt32Type { get; }
    public TypeAnalysisContext SystemUInt32Type { get; }
    public TypeAnalysisContext SystemInt64Type { get; }
    public TypeAnalysisContext SystemUInt64Type { get; }
    public TypeAnalysisContext SystemSingleType { get; }
    public TypeAnalysisContext SystemDoubleType { get; }
    public TypeAnalysisContext SystemIntPtrType { get; }
    public TypeAnalysisContext SystemUIntPtrType { get; }
    public TypeAnalysisContext SystemExceptionType { get; }
    public TypeAnalysisContext SystemStringType { get; }
    public TypeAnalysisContext SystemTypedReferenceType { get; }
    public TypeAnalysisContext SystemTypeType { get; }
    public TypeAnalysisContext SystemAttributeType { get; }
    public TypeAnalysisContext? UnmanagedCallersOnlyAttributeType { get; }
    public TypeAnalysisContext EnumType { get; private set; }

    public SystemTypesContext(ApplicationAnalysisContext appContext)
    {
        _appContext = appContext;

        var systemAssembly = _appContext.GetAssemblyByName("mscorlib") ?? throw new("Could not find system assembly");

        SystemObjectType = systemAssembly.GetTypeByFullName("System.Object")!;
        SystemVoidType = systemAssembly.GetTypeByFullName("System.Void")!;

        SystemBooleanType = systemAssembly.GetTypeByFullName("System.Boolean")!;
        SystemCharType = systemAssembly.GetTypeByFullName("System.Char")!;

        SystemSByteType = systemAssembly.GetTypeByFullName("System.SByte")!;
        SystemByteType = systemAssembly.GetTypeByFullName("System.Byte")!;

        SystemInt16Type = systemAssembly.GetTypeByFullName("System.Int16")!;
        SystemUInt16Type = systemAssembly.GetTypeByFullName("System.UInt16")!;

        SystemInt32Type = systemAssembly.GetTypeByFullName("System.Int32")!;
        SystemUInt32Type = systemAssembly.GetTypeByFullName("System.UInt32")!;

        SystemInt64Type = systemAssembly.GetTypeByFullName("System.Int64")!;
        SystemUInt64Type = systemAssembly.GetTypeByFullName("System.UInt64")!;

        SystemSingleType = systemAssembly.GetTypeByFullName("System.Single")!;
        SystemDoubleType = systemAssembly.GetTypeByFullName("System.Double")!;

        SystemIntPtrType = systemAssembly.GetTypeByFullName("System.IntPtr")!;
        SystemUIntPtrType = systemAssembly.GetTypeByFullName("System.UIntPtr")!;

        SystemStringType = systemAssembly.GetTypeByFullName("System.String")!;
        SystemTypedReferenceType = systemAssembly.GetTypeByFullName("System.TypedReference")!;
        SystemTypeType = systemAssembly.GetTypeByFullName("System.Type")!;

        SystemExceptionType = systemAssembly.GetTypeByFullName("System.Exception")!;
        SystemAttributeType = systemAssembly.GetTypeByFullName("System.Attribute")!;
        
        UnmanagedCallersOnlyAttributeType = systemAssembly.GetTypeByFullName("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute");
        
        EnumType = systemAssembly.GetTypeByFullName("System.Enum")!;
    }

    public bool TryGetIl2CppTypeEnum(TypeAnalysisContext context, out Il2CppTypeEnum value)
    {
        if (context == SystemBooleanType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN;
        else if (context == SystemCharType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_CHAR;
        else if (context == SystemSByteType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_I1;
        else if (context == SystemByteType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_U1;
        else if (context == SystemInt16Type)
            value = Il2CppTypeEnum.IL2CPP_TYPE_I2;
        else if (context == SystemUInt16Type)
            value = Il2CppTypeEnum.IL2CPP_TYPE_U2;
        else if (context == SystemInt32Type)
            value = Il2CppTypeEnum.IL2CPP_TYPE_I4;
        else if (context == SystemUInt32Type)
            value = Il2CppTypeEnum.IL2CPP_TYPE_U4;
        else if (context == SystemInt64Type)
            value = Il2CppTypeEnum.IL2CPP_TYPE_I8;
        else if (context == SystemUInt64Type)
            value = Il2CppTypeEnum.IL2CPP_TYPE_U8;
        else if (context == SystemSingleType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_R4;
        else if (context == SystemDoubleType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_R8;
        else if (context == SystemIntPtrType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_I;
        else if (context == SystemUIntPtrType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_U;
        else if (context == SystemStringType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_STRING;
        else if (context == SystemTypedReferenceType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF;
        else if (context == SystemObjectType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_OBJECT;
        else if (context == SystemVoidType)
            value = Il2CppTypeEnum.IL2CPP_TYPE_VOID;
        else
        {
            value = default;
            return false;
        }
        return true;
    }

    public TypeAnalysisContext GetPrimitive(Il2CppTypeEnum type) => type switch
    {
        Il2CppTypeEnum.IL2CPP_TYPE_OBJECT => SystemObjectType,
        Il2CppTypeEnum.IL2CPP_TYPE_VOID => SystemVoidType,
        Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => SystemBooleanType,
        Il2CppTypeEnum.IL2CPP_TYPE_CHAR => SystemCharType,
        Il2CppTypeEnum.IL2CPP_TYPE_I1 => SystemSByteType,
        Il2CppTypeEnum.IL2CPP_TYPE_U1 => SystemByteType,
        Il2CppTypeEnum.IL2CPP_TYPE_I2 => SystemInt16Type,
        Il2CppTypeEnum.IL2CPP_TYPE_U2 => SystemUInt16Type,
        Il2CppTypeEnum.IL2CPP_TYPE_I4 => SystemInt32Type,
        Il2CppTypeEnum.IL2CPP_TYPE_U4 => SystemUInt32Type,
        Il2CppTypeEnum.IL2CPP_TYPE_I => SystemIntPtrType,
        Il2CppTypeEnum.IL2CPP_TYPE_U => SystemUIntPtrType,
        Il2CppTypeEnum.IL2CPP_TYPE_I8 => SystemInt64Type,
        Il2CppTypeEnum.IL2CPP_TYPE_U8 => SystemUInt64Type,
        Il2CppTypeEnum.IL2CPP_TYPE_R4 => SystemSingleType,
        Il2CppTypeEnum.IL2CPP_TYPE_R8 => SystemDoubleType,
        Il2CppTypeEnum.IL2CPP_TYPE_STRING => SystemStringType,
        Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF => SystemTypedReferenceType,
        Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX => SystemTypeType,
        _ => throw new ArgumentException("Type is not a primitive", nameof(type))
    };
}
