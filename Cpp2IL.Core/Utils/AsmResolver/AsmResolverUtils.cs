using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Utils.AsmResolver;

public static class AsmResolverUtils
{
    private static readonly ConcurrentDictionary<string, TypeDefinition?> CachedTypeDefsByName = new();
    private static readonly ConcurrentDictionary<string, TypeSignature?> CachedTypeSignaturesByName = new();

    public static readonly ConcurrentDictionary<Il2CppVariableWidthIndex<Il2CppTypeDefinition>, TypeDefinition> TypeDefsByIndex = new();

    internal static void Reset()
    {
        CachedTypeDefsByName.Clear();
        CachedTypeSignaturesByName.Clear();
        TypeDefsByIndex.Clear();
    }

    public static TypeDefinition GetPrimitiveTypeDef(Il2CppTypeEnum type) =>
        type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_OBJECT => TypeDefinitionsAsmResolver.Object,
            Il2CppTypeEnum.IL2CPP_TYPE_VOID => TypeDefinitionsAsmResolver.Void,
            Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => TypeDefinitionsAsmResolver.Boolean,
            Il2CppTypeEnum.IL2CPP_TYPE_CHAR => TypeDefinitionsAsmResolver.Char,
            Il2CppTypeEnum.IL2CPP_TYPE_I1 => TypeDefinitionsAsmResolver.SByte,
            Il2CppTypeEnum.IL2CPP_TYPE_U1 => TypeDefinitionsAsmResolver.Byte,
            Il2CppTypeEnum.IL2CPP_TYPE_I2 => TypeDefinitionsAsmResolver.Int16,
            Il2CppTypeEnum.IL2CPP_TYPE_U2 => TypeDefinitionsAsmResolver.UInt16,
            Il2CppTypeEnum.IL2CPP_TYPE_I4 => TypeDefinitionsAsmResolver.Int32,
            Il2CppTypeEnum.IL2CPP_TYPE_U4 => TypeDefinitionsAsmResolver.UInt32,
            Il2CppTypeEnum.IL2CPP_TYPE_I => TypeDefinitionsAsmResolver.IntPtr,
            Il2CppTypeEnum.IL2CPP_TYPE_U => TypeDefinitionsAsmResolver.UIntPtr,
            Il2CppTypeEnum.IL2CPP_TYPE_I8 => TypeDefinitionsAsmResolver.Int64,
            Il2CppTypeEnum.IL2CPP_TYPE_U8 => TypeDefinitionsAsmResolver.UInt64,
            Il2CppTypeEnum.IL2CPP_TYPE_R4 => TypeDefinitionsAsmResolver.Single,
            Il2CppTypeEnum.IL2CPP_TYPE_R8 => TypeDefinitionsAsmResolver.Double,
            Il2CppTypeEnum.IL2CPP_TYPE_STRING => TypeDefinitionsAsmResolver.String,
            Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF => TypeDefinitionsAsmResolver.TypedReference,
            Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX => TypeDefinitionsAsmResolver.Type,
            _ => throw new ArgumentException($"Type is not a primitive - {type}", nameof(type))
        };

    public static TypeDefinition? TryLookupTypeDefKnownNotGeneric(string? name)
    {
        if (name == null)
            return null;

        if (TypeDefinitionsAsmResolver.GetPrimitive(name) is { } primitive)
            return primitive;

        var key = name.ToLower(CultureInfo.InvariantCulture);

        if (CachedTypeDefsByName.TryGetValue(key, out var ret))
            return ret;

        var definedType = Cpp2IlApi.CurrentAppContext!.AllTypes.FirstOrDefault(t => string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase));

        //Try subclasses
        definedType ??= Cpp2IlApi.CurrentAppContext.AllTypes.FirstOrDefault(t =>
        {
            return t.Definition?.FullName != null
                && t.Definition.FullName.Contains('/')
                && string.Equals(t.Definition.FullName.Replace('/', '.'), name, StringComparison.OrdinalIgnoreCase);
        });

        ret = definedType?.GetExtraData<TypeDefinition>("AsmResolverType");
        
        if (ret == null)
            return null;
        
        CachedTypeDefsByName.TryAdd(key, ret);
        return ret;
    }

    public static TypeSignature? TryLookupTypeSignatureByName(string? name, ReadOnlySpan<string> genericParameterNames = default)
    {
        if (name == null)
            return null;

        var key = name.ToLower(CultureInfo.InvariantCulture);

        if (genericParameterNames.Length == 0 && CachedTypeSignaturesByName.TryGetValue(key, out var ret))
            return ret;

        var result = InternalTryLookupTypeSignatureByName(name, genericParameterNames);

        if (genericParameterNames.Length == 0)
            CachedTypeSignaturesByName.TryAdd(key, result);

        return result;
    }

    private static TypeSignature? InternalTryLookupTypeSignatureByName(string name, ReadOnlySpan<string> genericParameterNames = default)
    {
        if (TypeDefinitionsAsmResolver.GetPrimitive(name) is { } primitive)
            return primitive.ToTypeSignature();

        //The only real cases we end up here are:
        //From explicit override resolving, because that has to be done by name
        //Sometimes in attribute restoration if we come across an object parameter, but this almost always has to be a system or cecil type, or an enum.
        //While originally initializing the TypeDefinitions class, which is always a system type
        //And during exception helper location, which is always a system type.
        //So really the only remapping we should have to handle is during explicit override restoration.

        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            var without = name[..^2];
            var result = InternalTryLookupTypeSignatureByName(without, genericParameterNames);
            return result?.MakeSzArrayType();
        }

        var parsedType = Parse(name);

        // Arrays should be handled above
        Debug.Assert(parsedType.Suffix is "");

        var genericParameterIndex = genericParameterNames.IndexOf(parsedType.BaseType);
        if (genericParameterIndex >= 0)
            return new GenericParameterSignature(GenericParameterType.Type, genericParameterIndex);

        var baseType = TryLookupTypeDefKnownNotGeneric(parsedType.BaseType);
        if (baseType == null)
            return null;

        if (parsedType.GenericArguments.Length == 0)
            return baseType.ToTypeSignature();

        var typeArguments = new TypeSignature[parsedType.GenericArguments.Length];
        for (var i = 0; i < parsedType.GenericArguments.Length; i++)
        {
            var typeArgument = InternalTryLookupTypeSignatureByName(parsedType.GenericArguments[i], genericParameterNames);
            if (typeArgument == null)
                return null;
            typeArguments[i] = typeArgument;
        }

        var runtimeContext = Cpp2IlApi.CurrentAppContext!.GetExtraData<RuntimeContext>("RuntimeContext") ?? throw new("AsmResolver runtime context not found in application analysis context");
        return baseType.MakeGenericInstanceType(runtimeContext, typeArguments);
    }

    private readonly record struct ParsedTypeString(string BaseType, string Suffix, string[] GenericArguments);

    private static ParsedTypeString Parse(string name)
    {
        var firstAngleBracket = name.IndexOf('<');
        if (firstAngleBracket < 0)
        {
            var firstSquareBracket = name.IndexOf('[');
            if (firstSquareBracket < 0)
                return new ParsedTypeString(name, "", []);
            else
                return new ParsedTypeString(name[..firstSquareBracket], name[(firstSquareBracket + 1)..], []);
        }

        var lastAngleBracket = name.LastIndexOf('>');
        var genericParams = MiscUtils.GetGenericParams(name[(firstAngleBracket + 1)..(lastAngleBracket)]);

        var baseType = $"{name[..firstAngleBracket]}`{genericParams.Length}";
        var suffix = name[(lastAngleBracket + 1)..];

        return new ParsedTypeString(baseType, suffix, genericParams);
    }

    public static ITypeDefOrRef ImportTypeIfNeeded(this ReferenceImporter importer, ITypeDefOrRef type)
    {
        if (type is TypeSpecification spec)
            return new TypeSpecification(importer.ImportTypeSignature(spec.Signature!));

        return importer.ImportType(type);
    }

    internal static ArrayTypeSignature MakeArrayTypeWithLowerBounds(this TypeSignature elementType, int rank)
    {
        var result = new ArrayTypeSignature(elementType, rank);
        for (var i = 0; i < rank; i++)
            result.Dimensions[i] = new ArrayDimension(null, 0);

        return result;
    }
}
