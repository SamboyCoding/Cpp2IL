using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.Reflection;

public static class LibCpp2IlReflection
{
    private static readonly ConcurrentDictionary<(string, string?), Il2CppTypeDefinition?> CachedTypes = new();
    private static readonly ConcurrentDictionary<string, Il2CppTypeDefinition?> CachedTypesByFullName = new();

    private static readonly Dictionary<Il2CppTypeDefinition, int> TypeIndices = new();
    private static readonly Dictionary<Il2CppMethodDefinition, int> MethodIndices = new();
    private static readonly Dictionary<Il2CppFieldDefinition, int> FieldIndices = new();
    private static readonly Dictionary<Il2CppPropertyDefinition, int> PropertyIndices = new();

    private static readonly Dictionary<Il2CppTypeEnum, Il2CppType> PrimitiveTypeCache = new();
    public static readonly Dictionary<Il2CppTypeEnum, Il2CppTypeDefinition> PrimitiveTypeDefinitions = new();
    private static readonly Dictionary<long, Il2CppType> Il2CppTypeCache = new();

    internal static void ResetCaches()
    {
        CachedTypes.Clear();
        CachedTypesByFullName.Clear();

        lock (TypeIndices)
            TypeIndices.Clear();

        MethodIndices.Clear();
        FieldIndices.Clear();
        PropertyIndices.Clear();
        PrimitiveTypeCache.Clear();
        PrimitiveTypeDefinitions.Clear();
        Il2CppTypeCache.Clear();
    }

    internal static void InitCaches()
    {
        for (var e = Il2CppTypeEnum.IL2CPP_TYPE_VOID; e <= Il2CppTypeEnum.IL2CPP_TYPE_STRING; e++)
        {
            PrimitiveTypeCache[e] = LibCpp2IlMain.Binary!.AllTypes.First(t => t.Type == e && t.Byref == 0);
        }

        PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF] = LibCpp2IlMain.Binary!.AllTypes.First(t => t.Type == Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF && t.Byref == 0);
        for (var e = Il2CppTypeEnum.IL2CPP_TYPE_I; e <= Il2CppTypeEnum.IL2CPP_TYPE_U; e++)
        {
            PrimitiveTypeCache[e] = LibCpp2IlMain.Binary!.AllTypes.First(t => t.Type == e && t.Byref == 0);
        }

        PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_OBJECT] = LibCpp2IlMain.Binary!.AllTypes.First(t => t.Type == Il2CppTypeEnum.IL2CPP_TYPE_OBJECT && t.Byref == 0);

        for (var i = 0; i < LibCpp2IlMain.TheMetadata!.typeDefs.Length; i++)
        {
            var typeDefinition = LibCpp2IlMain.TheMetadata.typeDefs[i];

            TypeIndices[typeDefinition] = i;

            var type = LibCpp2IlMain.Binary!.AllTypes[typeDefinition.ByvalTypeIndex];

            if (type.Type.IsIl2CppPrimitive())
                PrimitiveTypeDefinitions[type.Type] = typeDefinition;
        }

        foreach (var type in LibCpp2IlMain.Binary!.AllTypes)
        {
            if (type.Type is not Il2CppTypeEnum.IL2CPP_TYPE_CLASS and not Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                continue;

            if (type.Byref == 0)
            {
                Il2CppTypeCache[type.Data.ClassIndex] = type;
            }
        }
    }

    public static Il2CppTypeDefinition? GetType(string name, string? @namespace = null)
    {
        if (LibCpp2IlMain.TheMetadata == null) return null;

        var key = (name, @namespace);
        if (!CachedTypes.ContainsKey(key))
        {
            var typeDef = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(td =>
                td.Name == name &&
                (@namespace == null || @namespace == td.Namespace)
            );
            CachedTypes[key] = typeDef;
        }

        return CachedTypes[key];
    }

    public static Il2CppTypeDefinition? GetTypeByFullName(string fullName)
    {
        if (LibCpp2IlMain.TheMetadata == null) return null;

        if (!CachedTypesByFullName.ContainsKey(fullName))
        {
            var typeDef = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(td =>
                td.FullName == fullName
            );
            CachedTypesByFullName[fullName] = typeDef;
        }

        return CachedTypesByFullName[fullName];
    }


    public static Il2CppTypeDefinition? GetTypeDefinitionByTypeIndex(int index)
    {
        if (LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null) return null;

        if (index >= LibCpp2IlMain.Binary.NumTypes || index < 0) return null;

        var type = LibCpp2IlMain.Binary.GetType(index);

        return LibCpp2IlMain.TheMetadata.typeDefs[type.Data.ClassIndex];
    }

    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
    public static int GetTypeIndexFromType(Il2CppTypeDefinition typeDefinition)
    {
        if (LibCpp2IlMain.TheMetadata == null) return -1;

        return TypeIndices.GetOrDefault(typeDefinition, -1);
    }

    // ReSharper disable InconsistentlySynchronizedField
    public static int GetMethodIndexFromMethod(Il2CppMethodDefinition methodDefinition)
    {
        if (LibCpp2IlMain.TheMetadata == null) return -1;

        if (MethodIndices.Count == 0)
        {
            lock (MethodIndices)
            {
                if (MethodIndices.Count == 0)
                {
                    //Check again inside lock
                    for (var i = 0; i < LibCpp2IlMain.TheMetadata.methodDefs.Length; i++)
                    {
                        var def = LibCpp2IlMain.TheMetadata.methodDefs[i];
                        MethodIndices[def] = i;
                    }
                }
            }
        }

        return MethodIndices.GetOrDefault(methodDefinition, -1);
    }

    // ReSharper disable InconsistentlySynchronizedField
    public static int GetFieldIndexFromField(Il2CppFieldDefinition fieldDefinition)
    {
        if (LibCpp2IlMain.TheMetadata == null) return -1;

        if (FieldIndices.Count == 0)
        {
            lock (FieldIndices)
            {
                if (FieldIndices.Count == 0)
                {
                    for (var i = 0; i < LibCpp2IlMain.TheMetadata.fieldDefs.Length; i++)
                    {
                        var def = LibCpp2IlMain.TheMetadata.fieldDefs[i];
                        FieldIndices[def] = i;
                    }
                }
            }
        }

        return FieldIndices[fieldDefinition];
    }

    public static int GetPropertyIndexFromProperty(Il2CppPropertyDefinition propertyDefinition)
    {
        if (LibCpp2IlMain.TheMetadata == null) return -1;

        if (PropertyIndices.Count == 0)
        {
            lock (PropertyIndices)
            {
                if (PropertyIndices.Count == 0)
                {
                    for (var i = 0; i < LibCpp2IlMain.TheMetadata.propertyDefs.Length; i++)
                    {
                        var def = LibCpp2IlMain.TheMetadata.propertyDefs[i];
                        PropertyIndices[def] = i;
                    }
                }
            }
        }

        return PropertyIndices[propertyDefinition];
    }

    public static Il2CppType? GetTypeFromDefinition(Il2CppTypeDefinition definition)
    {
        if (LibCpp2IlMain.Binary == null)
            return null;

        switch (definition.FullName)
        {
            case "System.SByte":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I1];
            case "System.Int16":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I2];
            case "System.Int32":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I4];
            case "System.Int64":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I8];
            case "System.Byte":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U1];
            case "System.UInt16":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U2];
            case "System.UInt32":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U4];
            case "System.UInt64":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U8];
            case "System.IntPtr":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I];
            case "System.UIntPtr":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U];
            case "System.Single":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_R4];
            case "System.Double":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_R8];
            case "System.Boolean":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN];
            case "System.Char":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_CHAR];
            case "System.String":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_STRING];
            case "System.Void":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_VOID];
            case "System.TypedReference":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF];
            case "System.Object":
                return PrimitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_OBJECT];
        }

        var index = definition.TypeIndex;

        if (Il2CppTypeCache.TryGetValue(index, out var cachedType))
        {
            return cachedType;
        }

        foreach (var type in LibCpp2IlMain.Binary.AllTypes)
        {
            if (type.Type is not Il2CppTypeEnum.IL2CPP_TYPE_CLASS and not Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                continue;

            if (type.Data.ClassIndex == index && type.Byref == 0)
            {
                lock (Il2CppTypeCache)
                {
                    Il2CppTypeCache[index] = type;
                }

                return type;
            }
        }

        return null;
    }
}
