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

    internal static string GetTypeName(Il2CppMetadata metadata, Il2CppBinary cppAssembly, Il2CppTypeDefinition typeDef, bool fullName = false)
    {
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
            ret += GetTypeName(metadata, cppAssembly, cppAssembly.GetType(typeDef.DeclaringTypeIndex)) + ".";
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

    internal static Il2CppTypeReflectionData[]? GetGenericTypeParams(Il2CppGenericInst genericInst)
    {
        var binary = genericInst.OwningBinary;
        if (binary == null) return null;

        var types = new Il2CppTypeReflectionData[genericInst.pointerCount];
        var pointers = binary.ReadNUintArrayAtVirtualAddress(genericInst.pointerStart, (long)genericInst.pointerCount);
        for (uint i = 0; i < genericInst.pointerCount; ++i)
        {
            var oriType = binary.GetIl2CppTypeFromPointer(pointers[i]);
            types[i] = GetTypeReflectionData(oriType);
        }

        return types;
    }

    internal static string GetGenericTypeParamNames(Il2CppMetadata metadata, Il2CppBinary cppAssembly, Il2CppGenericInst genericInst)
    {
        var typeNames = genericInst.Types.Select(t => GetTypeName(metadata, cppAssembly, t)).ToArray();

        return $"<{string.Join(", ", typeNames)}>";
    }

    public static string GetTypeName(Il2CppMetadata metadata, Il2CppBinary cppAssembly, Il2CppType type, bool fullName = false)
    {
        string ret;
        switch (type.Type)
        {
            case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
            case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
            {
                var typeDef = type.AsClass();
                ret = string.Empty;

                ret += GetTypeName(metadata, cppAssembly, typeDef, fullName);
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
            {
                var genericClass = cppAssembly.ReadReadableAtVirtualAddress<Il2CppGenericClass>(type.Data.GenericClass);
                var typeDef = genericClass.TypeDefinition;
                ret = typeDef.Name!;
                var genericInst = genericClass.Context.ClassInst!;
                ret = ret.Replace($"`{genericInst.pointerCount}", "");
                ret += GetGenericTypeParamNames(metadata, cppAssembly, genericInst);
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
                var arrayType = cppAssembly.ReadReadableAtVirtualAddress<Il2CppArrayType>(type.Data.Array);
                var oriType = arrayType.GetElementTypeOrThrow();
                ret = $"{GetTypeName(metadata, cppAssembly, oriType)}[{new string(',', arrayType.rank - 1)}]";
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
            {
                var oriType = cppAssembly.GetIl2CppTypeFromPointer(type.Data.Type);
                ret = $"{GetTypeName(metadata, cppAssembly, oriType)}[]";
                break;
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
            {
                var oriType = cppAssembly.GetIl2CppTypeFromPointer(type.Data.Type);
                ret = $"{GetTypeName(metadata, cppAssembly, oriType)}*";
                break;
            }
            default:
                ret = GetTypeName(type.Type);
                break;
        }

        return ret;
    }

    internal static object? GetDefaultValue(Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> dataIndex, Il2CppVariableWidthIndex<Il2CppType> typeIndex, Il2CppMetadata metadata, Il2CppBinary binary)
    {
        if (dataIndex.IsNull)
            return null; //Literally null.

        var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
        if (pointer <= 0) return null;

        var defaultValueType = binary.GetType(typeIndex);
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
        return new()
        {
            baseType = what, genericParams = [], isGenericType = false, isType = true,
        };
    }

    public static Il2CppTypeReflectionData GetTypeReflectionData(Il2CppType forWhat)
    {
#pragma warning disable CS0618 // Fallback to legacy statics for backwards compatibility
        var binary = forWhat.OwningBinary ?? LibCpp2IlMain.Binary;
        var metadata = forWhat.OwningMetadata ?? LibCpp2IlMain.TheMetadata;
#pragma warning restore CS0618

        if (binary == null || metadata == null)
            throw new Exception("Can't get type reflection data when not initialized. How did you even get the type?");

        switch (forWhat.Type)
        {
            case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                return WrapType(LibCpp2IlReflection.GetType("Object", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                return WrapType(LibCpp2IlReflection.GetType("Void", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                return WrapType(LibCpp2IlReflection.GetType("Boolean", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                return WrapType(LibCpp2IlReflection.GetType("Char", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                return WrapType(LibCpp2IlReflection.GetType("SByte", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                return WrapType(LibCpp2IlReflection.GetType("Byte", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                return WrapType(LibCpp2IlReflection.GetType("Int16", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                return WrapType(LibCpp2IlReflection.GetType("UInt16", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                return WrapType(LibCpp2IlReflection.GetType("Int32", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                return WrapType(LibCpp2IlReflection.GetType("UInt32", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I:
                return WrapType(LibCpp2IlReflection.GetType("IntPtr", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U:
                return WrapType(LibCpp2IlReflection.GetType("UIntPtr", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                return WrapType(LibCpp2IlReflection.GetType("Int64", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                return WrapType(LibCpp2IlReflection.GetType("UInt64", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                return WrapType(LibCpp2IlReflection.GetType("Single", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                return WrapType(LibCpp2IlReflection.GetType("Double", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                return WrapType(LibCpp2IlReflection.GetType("String", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                return WrapType(LibCpp2IlReflection.GetType("TypedReference", "System")!);
            case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
            case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                //"normal" type
                return new Il2CppTypeReflectionData
                {
                    baseType = forWhat.AsClass(), genericParams = [], isType = true, isGenericType = false,
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

                return new()
                {
                    baseType = typeDefinition, genericParams = genericParams.ToArray(), isType = true, isGenericType = true,
                };
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
            case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
            {
                var param = metadata.GetGenericParameterFromIndex(forWhat.Data.GenericParameterIndex);
                var genericName = metadata.GetStringFromIndex(param.nameIndex);

                return new()
                {
                    baseType = null,
                    genericParams = [],
                    isType = false,
                    isGenericType = false,
                    variableGenericParamName = genericName,
                    variableGenericParamIndex = forWhat.Data.GenericParameterIndex,
                };
            }
            case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
            {
                var oriType = binary.GetIl2CppTypeFromPointer(forWhat.Data.Type);
                return new()
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
                var oriType = arrayType.GetElementTypeOrThrow();
                return new()
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
