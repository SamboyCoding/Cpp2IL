using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL;

public static class LibCpp2ILUtils
{
    private static readonly Dictionary<string, ulong> PrimitiveSizes = new()
    {
        { "Byte", 1 },
        { "SByte", 1 },
        { "Boolean", 1 },
        { "Int16", 2 },
        { "UInt16", 2 },
        { "Char", 2 },
        { "Int32", 4 },
        { "UInt32", 4 },
        { "Single", 4 },
        { "Int64", 8 },
        { "UInt64", 8 },
        { "Double", 8 },
        { "IntPtr", 8 },
        { "UIntPtr", 8 },
    };

    public static string GetTypeName(Il2CppTypeEnum type) => (int)type switch
    {
        1 => "void",
        2 => "bool",
        3 => "char",
        4 => "sbyte",
        5 => "byte",
        6 => "short",
        7 => "ushort",
        8 => "int",
        9 => "uint",
        10 => "long",
        11 => "ulong",
        12 => "float",
        13 => "double",
        14 => "string",
        22 => "TypedReference",
        24 => "IntPtr",
        25 => "UIntPtr",
        28 => "object",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    internal static string GetTypeName(LibCpp2IlContext context, Il2CppTypeDefinition typeDef, bool fullName = false)
    {
        var metadata = context.Metadata;
        var binary = context.Binary;

        var ret = string.Empty;
        if (fullName)
        {
            ret = typeDef.Namespace;
            if (ret != string.Empty)
            {
                ret += ".";
            }
        }

        if (typeDef.DeclaringTypeIndex.IsNonNull)
        {
            ret += GetTypeName(context, binary.GetType(typeDef.DeclaringTypeIndex)) + ".";
        }

        ret += metadata.GetStringFromIndex(typeDef.NameIndex);
        var names = new List<string>();
        if (typeDef.GenericContainer is not {} genericContainer)
            return ret;

        foreach (var parameter in genericContainer.GenericParameters)
        {
            names.Add(metadata.GetStringFromIndex(parameter.nameIndex));
        }

        ret = ret.Replace($"`{genericContainer.genericParameterCount}", "");
        ret += $"<{string.Join(", ", names)}>";

        return ret;
    }

    internal static Il2CppTypeReflectionData[] GetGenericTypeParams(Il2CppGenericInst genericInst)
    {
        var binary = genericInst.OwningContext.Binary;

        var types = new Il2CppTypeReflectionData[genericInst.pointerCount];
        var pointers = binary.ReadNUintArrayAtVirtualAddress(genericInst.pointerStart, (long)genericInst.pointerCount);
        for (uint i = 0; i < genericInst.pointerCount; ++i)
        {
            var oriType = binary.GetIl2CppTypeFromPointer(pointers[i]);
            types[i] = GetTypeReflectionData(oriType);
        }

        return types;
    }

    internal static string GetGenericTypeParamNames(LibCpp2IlContext context, Il2CppGenericInst genericInst)
    {
        var typeNames = genericInst.Types.Select(t => GetTypeName(context, t)).ToArray();

        return $"<{string.Join(", ", typeNames)}>";
    }

    public static string GetTypeName(LibCpp2IlContext context, Il2CppType type, bool fullName = false)
    {
        var metadata = context.Metadata;
        var binary = context.Binary;

        string ret;
        switch (type.Type)
        {
            case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
            case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
            {
                var typeDef = type.AsClass();
                ret = string.Empty;

                ret += GetTypeName(context, typeDef, fullName);
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
            {
                var genericClass = binary.ReadReadableAtVirtualAddress<Il2CppGenericClass>(type.Data.GenericClass);
                var typeDef = genericClass.TypeDefinition;
                ret = typeDef.Name!;
                var genericInst = genericClass.Context.ClassInst!;
                ret = ret.Replace($"`{genericInst.pointerCount}", "");
                ret += GetGenericTypeParamNames(context, genericInst);
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
            case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
            {
                var param = metadata.GetGenericParameterFromIndex(type.Data.GenericParameterIndex);
                ret = metadata.GetStringFromIndex(param.nameIndex);
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
            {
                var arrayType = binary.ReadReadableAtVirtualAddress<Il2CppArrayType>(type.Data.Array);
                var oriType = arrayType.ElementType;
                ret = $"{GetTypeName(context, oriType)}[{new string(',', arrayType.rank - 1)}]";
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
            {
                var oriType = binary.GetIl2CppTypeFromPointer(type.Data.Type);
                ret = $"{GetTypeName(context, oriType)}[]";
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
            {
                var oriType = binary.GetIl2CppTypeFromPointer(type.Data.Type);
                ret = $"{GetTypeName(context, oriType)}*";
                break;
            }
            default:
                ret = GetTypeName(type.Type);
                break;
        }

        return ret;
    }

    internal static object? GetDefaultValue(Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> dataIndex, Il2CppVariableWidthIndex<Il2CppType> typeIndex, LibCpp2IlContext context)
    {
        var metadata = context.Metadata;

        if (dataIndex.IsNull)
            return null; //Literally null.

        var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
        if (pointer <= 0) return null;

        var defaultValueType = context.Binary.GetType(typeIndex);
        metadata.GetLockOrThrow();
        metadata.Position = pointer;
        try
        {
            switch (defaultValueType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return metadata.ReadBoolean();
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return metadata.ReadByte();
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return metadata.ReadSByte();
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return BitConverter.ToChar(metadata.ReadByteArrayAtRawAddressNoLock(pointer, 2), 0);
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return metadata.ReadUInt16();
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return metadata.ReadInt16();
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    if (metadata.MetadataVersion < 29)
                        return metadata.ReadUInt32();
                    return metadata.ReadUnityCompressedUIntAtRawAddrNoLock(pointer, out _);
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    if (metadata.MetadataVersion < 29)
                        return metadata.ReadInt32();
                    return metadata.ReadUnityCompressedIntAtRawAddr(pointer, false, out _);
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return metadata.ReadUInt64();
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return metadata.ReadInt64();
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return metadata.ReadSingle();
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return metadata.ReadDouble();
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    int len;
                    var lenLen = 4;
                    if (metadata.MetadataVersion < 29)
                        len = metadata.ReadInt32();
                    else
                        len = metadata.ReadUnityCompressedIntAtRawAddr(pointer, false, out lenLen);
                    if (len > 1024 * 64)
                        LibLogger.WarnNewline("[GetDefaultValue] String length is really large: " + len);
                    return Encoding.UTF8.GetString(metadata.ReadByteArrayAtRawAddressNoLock(pointer + lenLen, len));
                default:
                    return null;
            }
        }
        finally
        {
            metadata.ReleaseLock();
        }
    }

    public static Il2CppTypeReflectionData WrapType(Il2CppTypeDefinition what)
    {
        return new(what.OwningContext)
        {
            baseType = what, genericParams = [], isGenericType = false, isType = true
        };
    }

    public static Il2CppTypeReflectionData GetTypeReflectionData(Il2CppType forWhat)
    {
        var context = forWhat.OwningContext;
        var binary = context.Binary;
        var metadata = context.Metadata;
        var reflectionCache = context.ReflectionCache;

        switch (forWhat.Type)
        {
            case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                return WrapType(reflectionCache.GetType("Object", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                return WrapType(reflectionCache.GetType("Void", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                return WrapType(reflectionCache.GetType("Boolean", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                return WrapType(reflectionCache.GetType("Char", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                return WrapType(reflectionCache.GetType("SByte", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                return WrapType(reflectionCache.GetType("Byte", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                return WrapType(reflectionCache.GetType("Int16", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                return WrapType(reflectionCache.GetType("UInt16", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                return WrapType(reflectionCache.GetType("Int32", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                return WrapType(reflectionCache.GetType("UInt32", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I:
                return WrapType(reflectionCache.GetType("IntPtr", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U:
                return WrapType(reflectionCache.GetType("UIntPtr", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                return WrapType(reflectionCache.GetType("Int64", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                return WrapType(reflectionCache.GetType("UInt64", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                return WrapType(reflectionCache.GetType("Single", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                return WrapType(reflectionCache.GetType("Double", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                return WrapType(reflectionCache.GetType("String", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                return WrapType(reflectionCache.GetType("TypedReference", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
            case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                //"normal" type
                return new(context)
                {
                    baseType = forWhat.AsClass(),
                    genericParams = [],
                    isType = true,
                    isGenericType = false
                };
            case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
            {
                //Generic type
                var genericClass = binary.ReadReadableAtVirtualAddress<Il2CppGenericClass>(forWhat.Data.GenericClass);

                //CHANGED IN v27: typeDefinitionIndex is a ptr to the type in the file.
                var typeDefinition = genericClass.TypeDefinition;

                var genericInst = genericClass.Context.ClassInst!;

                var genericParams = genericInst.Types
                    .Select(GetTypeReflectionData) //Recursive call here
                    .ToList();

                return new(context)
                {
                    baseType = typeDefinition,
                    genericParams = genericParams.ToArray(),
                    isType = true,
                    isGenericType = true
                };
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
            case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
            {
                var param = metadata.GetGenericParameterFromIndex(forWhat.Data.GenericParameterIndex);
                var genericName = metadata.GetStringFromIndex(param.nameIndex);

                return new(context)
                {
                    baseType = null,
                    genericParams = [],
                    isType = false,
                    isGenericType = false,
                    variableGenericParamName = genericName,
                    variableGenericParamIndex = forWhat.Data.GenericParameterIndex
                };
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
            {
                var oriType = binary.GetIl2CppTypeFromPointer(forWhat.Data.Type);
                return new(context)
                {
                    baseType = null,
                    arrayType = GetTypeReflectionData(oriType),
                    arrayRank = 1,
                    isArray = true,
                    isType = false,
                    isGenericType = false,
                    genericParams = []
                };
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
            {
                var arrayType = binary.ReadReadableAtVirtualAddress<Il2CppArrayType>(forWhat.Data.Array);
                var oriType = arrayType.ElementType;
                return new(context)
                {
                    baseType = null,
                    arrayType = GetTypeReflectionData(oriType),
                    isArray = true,
                    isType = false,
                    arrayRank = arrayType.rank,
                    isGenericType = false,
                    genericParams = []
                };
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
            {
                var oriType = binary.GetIl2CppTypeFromPointer(forWhat.Data.Type);
                var ret = GetTypeReflectionData(oriType);
                ret.isPointer = true;
                return ret;
            }
        }

        throw new ArgumentException($"Unknown type {forWhat.Type}");
    }

    internal static IEnumerable<int> Range(int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            yield return i;
        }
    }

    internal static void PopulateDeclaringAssemblyCache(Il2CppMetadata metadata)
    {
        foreach (var assembly in metadata.imageDefinitions)
        {
            foreach (var il2CppTypeDefinition in assembly.Types!)
            {
                il2CppTypeDefinition.DeclaringAssembly = assembly;
            }
        }
    }
}
