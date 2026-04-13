using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.Reflection;

/// <summary>
/// Per-context reflection caches. This is the non-static equivalent of <see cref="LibCpp2IlReflection"/>.
/// </summary>
public sealed class LibCpp2IlReflectionCache
{
    private readonly ConcurrentDictionary<(string, string?), Il2CppTypeDefinition?> _cachedTypes = new();
    private readonly ConcurrentDictionary<string, Il2CppTypeDefinition?> _cachedTypesByFullName = new();

    private readonly Dictionary<Il2CppTypeDefinition, Il2CppVariableWidthIndex<Il2CppTypeDefinition>> _typeIndices = new();
    private readonly Dictionary<Il2CppMethodDefinition, Il2CppVariableWidthIndex<Il2CppMethodDefinition>> _methodIndices = new();
    private readonly Dictionary<Il2CppFieldDefinition, Il2CppVariableWidthIndex<Il2CppFieldDefinition>> _fieldIndices = new();
    private readonly Dictionary<Il2CppPropertyDefinition, int> _propertyIndices = new();

    private readonly Dictionary<Il2CppFieldDefinition, Il2CppTypeDefinition> _fieldDeclaringTypes = new();

    private readonly Dictionary<Il2CppTypeEnum, Il2CppType> _primitiveTypeCache = new();
    public Dictionary<Il2CppTypeEnum, Il2CppTypeDefinition> PrimitiveTypeDefinitions { get; } = new();
    private readonly Dictionary<Il2CppVariableWidthIndex<Il2CppTypeDefinition>, Il2CppType> _il2CppTypeCache = new();

    private LibCpp2IlContext? _context;

    private LibCpp2IlContext Context =>
        _context ?? throw new InvalidOperationException("LibCpp2IlReflectionCache must be initialized before use");

    public void Reset()
    {
        _context = null;
        _cachedTypes.Clear();
        _cachedTypesByFullName.Clear();

        lock (_typeIndices)
            _typeIndices.Clear();

        _methodIndices.Clear();
        _fieldIndices.Clear();
        _propertyIndices.Clear();
        _fieldDeclaringTypes.Clear();
        _primitiveTypeCache.Clear();
        PrimitiveTypeDefinitions.Clear();
        _il2CppTypeCache.Clear();
    }

    internal void Init(LibCpp2IlContext context)
    {
        Reset();
        _context = context;

        for (var e = Il2CppTypeEnum.IL2CPP_TYPE_VOID; e <= Il2CppTypeEnum.IL2CPP_TYPE_STRING; e++)
            _primitiveTypeCache[e] = context.Binary.AllTypes.First(t => t.Type == e && t.Byref == 0);

        _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF] = context.Binary.AllTypes.FirstOrDefault(t => t.Type == Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF && t.Byref == 0)
            ?? context.Metadata.typeDefs.First(t => t.DeclaringAssembly?.Name is "mscorlib.dll" && t.Namespace is "System" && t.Name is "TypedReference").RawType;

        for (var e = Il2CppTypeEnum.IL2CPP_TYPE_I; e <= Il2CppTypeEnum.IL2CPP_TYPE_U; e++)
            _primitiveTypeCache[e] = context.Binary.AllTypes.First(t => t.Type == e && t.Byref == 0);

        _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_OBJECT] = context.Binary.AllTypes.First(t => t.Type == Il2CppTypeEnum.IL2CPP_TYPE_OBJECT && t.Byref == 0);

        for (var i = 0; i < context.Metadata.TypeDefinitionCount; i++)
        {
            var typeDefinition = context.Metadata.typeDefs[i];

            _typeIndices[typeDefinition] = Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage(i);

            var type = typeDefinition.RawType;

            if (type.Type.IsIl2CppPrimitive())
                PrimitiveTypeDefinitions[type.Type] = typeDefinition;
        }

        foreach (var type in context.Binary.AllTypes)
        {
            if (type.Type is not Il2CppTypeEnum.IL2CPP_TYPE_CLASS and not Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                continue;

            if (type.Byref == 0)
                _il2CppTypeCache[type.Data.ClassIndex] = type;
        }
    }

    public Il2CppTypeDefinition? GetType(string name, string? @namespace = null)
    {
        var key = (name, @namespace);
        if (!_cachedTypes.ContainsKey(key))
        {
            var typeDef = Context.Metadata.typeDefs.FirstOrDefault(td => td.Name == name && (@namespace == null || @namespace == td.Namespace));
            _cachedTypes[key] = typeDef;
        }

        return _cachedTypes[key];
    }

    public Il2CppTypeDefinition? GetTypeByFullName(string fullName)
    {
        if (!_cachedTypesByFullName.ContainsKey(fullName))
        {
            var typeDef = Context.Metadata.typeDefs.FirstOrDefault(td => td.FullName == fullName);
            _cachedTypesByFullName[fullName] = typeDef;
        }

        return _cachedTypesByFullName[fullName];
    }

    public Il2CppTypeDefinition? GetTypeDefinitionByTypeIndex(Il2CppVariableWidthIndex<Il2CppType> index)
    {
        if (index.IsNull) return null;

        var type = Context.Binary.GetType(index);
        return type.CoerceToUnderlyingTypeDefinition();
    }

    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
    public Il2CppVariableWidthIndex<Il2CppTypeDefinition> GetTypeIndexFromType(Il2CppTypeDefinition typeDefinition)
        => _typeIndices.GetOrDefault(typeDefinition, Il2CppVariableWidthIndex<Il2CppTypeDefinition>.Null);

    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> GetMethodIndexFromMethod(Il2CppMethodDefinition methodDefinition)
    {
        if (_methodIndices.Count == 0)
        {
            lock (_methodIndices)
            {
                if (_methodIndices.Count == 0)
                {
                    for (var i = 0; i < Context.Metadata.MethodDefinitionCount; i++)
                    {
                        var def = Context.Metadata.methodDefs[i];
                        _methodIndices[def] = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.MakeTemporaryForFixedWidthUsage(i);
                    }
                }
            }
        }

        return _methodIndices.GetOrDefault(methodDefinition, Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Null);
    }

    public Il2CppVariableWidthIndex<Il2CppFieldDefinition> GetFieldIndexFromField(Il2CppFieldDefinition fieldDefinition)
    {
        if (_fieldIndices.Count == 0)
        {
            lock (_fieldIndices)
            {
                if (_fieldIndices.Count == 0)
                {
                    for (var i = 0; i < Context.Metadata.fieldDefs.Length; i++)
                    {
                        var def = Context.Metadata.fieldDefs[i];
                        _fieldIndices[def] = Il2CppVariableWidthIndex<Il2CppFieldDefinition>.MakeTemporaryForFixedWidthUsage(i);
                    }
                }
            }
        }

        return _fieldIndices[fieldDefinition];
    }

    public int GetPropertyIndexFromProperty(Il2CppPropertyDefinition propertyDefinition)
    {
        if (_propertyIndices.Count == 0)
        {
            lock (_propertyIndices)
            {
                if (_propertyIndices.Count == 0)
                {
                    for (var i = 0; i < Context.Metadata.propertyDefs.Length; i++)
                        _propertyIndices[Context.Metadata.propertyDefs[i]] = i;
                }
            }
        }

        return _propertyIndices[propertyDefinition];
    }

    public Il2CppTypeDefinition GetDeclaringTypeFromField(Il2CppFieldDefinition fieldDefinition)
    {
        if (_fieldDeclaringTypes.Count == 0)
        {
            lock (_fieldDeclaringTypes)
            {
                if (_fieldDeclaringTypes.Count == 0)
                {
                    foreach (var declaringType in Context.Metadata.typeDefs)
                    foreach (var field in declaringType.Fields ?? [])
                        _fieldDeclaringTypes[field] = declaringType;
                }
            }
        }

        return _fieldDeclaringTypes[fieldDefinition];
    }

    public Il2CppType? GetTypeFromDefinition(Il2CppTypeDefinition definition)
    {
        switch (definition.FullName)
        {
            case "System.SByte": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I1];
            case "System.Int16": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I2];
            case "System.Int32": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I4];
            case "System.Int64": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I8];
            case "System.Byte": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U1];
            case "System.UInt16": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U2];
            case "System.UInt32": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U4];
            case "System.UInt64": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U8];
            case "System.IntPtr": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_I];
            case "System.UIntPtr": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_U];
            case "System.Single": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_R4];
            case "System.Double": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_R8];
            case "System.Boolean": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN];
            case "System.Char": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_CHAR];
            case "System.String": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_STRING];
            case "System.Void": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_VOID];
            case "System.TypedReference": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF];
            case "System.Object": return _primitiveTypeCache[Il2CppTypeEnum.IL2CPP_TYPE_OBJECT];
        }

        var index = definition.TypeIndex;

        if (_il2CppTypeCache.TryGetValue(index, out var cachedType))
            return cachedType;

        foreach (var type in Context.Binary.AllTypes)
        {
            if (type.Type is not Il2CppTypeEnum.IL2CPP_TYPE_CLASS and not Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                continue;

            if (type.Data.ClassIndex.Value == index.Value && type.Byref == 0)
            {
                lock (_il2CppTypeCache)
                    _il2CppTypeCache[index] = type;

                return type;
            }
        }

        return null;
    }
}
