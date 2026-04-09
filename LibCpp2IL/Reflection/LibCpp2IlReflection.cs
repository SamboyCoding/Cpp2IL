using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

#pragma warning disable CS0618 // This static facade intentionally uses LibCpp2IlMain.DefaultContext for backwards compatibility

namespace LibCpp2IL.Reflection;

public static class LibCpp2IlReflection
{
    private static LibCpp2IlReflectionCache? DefaultCache => LibCpp2IlMain.DefaultContext?.ReflectionCache;

    public static Dictionary<Il2CppTypeEnum, Il2CppTypeDefinition> PrimitiveTypeDefinitions
        => DefaultCache?.PrimitiveTypeDefinitions ?? throw new("LibCpp2IlReflection has not been initialized - no DefaultContext is set.");

    internal static void ResetCaches()
    {
        DefaultCache?.Reset();
    }

    internal static void InitCaches()
    {
        if (LibCpp2IlMain.DefaultContext == null)
            throw new("Cannot initialize reflection caches without a DefaultContext.");

        LibCpp2IlMain.DefaultContext.ReflectionCache.Init(LibCpp2IlMain.DefaultContext);
    }

    public static Il2CppTypeDefinition? GetType(string name, string? @namespace = null)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return null;

        return ctx.ReflectionCache.GetType(ctx.Metadata, name, @namespace);
    }

    public static Il2CppTypeDefinition? GetTypeByFullName(string fullName)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return null;

        return ctx.ReflectionCache.GetTypeByFullName(ctx.Metadata, fullName);
    }

    public static Il2CppTypeDefinition? GetTypeDefinitionByTypeIndex(Il2CppVariableWidthIndex<Il2CppType> index)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return null;

        return ctx.ReflectionCache.GetTypeDefinitionByTypeIndex(ctx.Binary, index);
    }

    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
    public static Il2CppVariableWidthIndex<Il2CppTypeDefinition> GetTypeIndexFromType(Il2CppTypeDefinition typeDefinition)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return Il2CppVariableWidthIndex<Il2CppTypeDefinition>.Null;

        return ctx.ReflectionCache.GetTypeIndexFromType(typeDefinition);
    }

    public static Il2CppVariableWidthIndex<Il2CppMethodDefinition> GetMethodIndexFromMethod(Il2CppMethodDefinition methodDefinition)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Null;

        return ctx.ReflectionCache.GetMethodIndexFromMethod(ctx.Metadata, methodDefinition);
    }

    public static Il2CppVariableWidthIndex<Il2CppFieldDefinition> GetFieldIndexFromField(Il2CppFieldDefinition fieldDefinition)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return Il2CppVariableWidthIndex<Il2CppFieldDefinition>.Null;

        return ctx.ReflectionCache.GetFieldIndexFromField(ctx.Metadata, fieldDefinition);
    }

    public static int GetPropertyIndexFromProperty(Il2CppPropertyDefinition propertyDefinition)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return -1;

        return ctx.ReflectionCache.GetPropertyIndexFromProperty(ctx.Metadata, propertyDefinition);
    }

    public static Il2CppTypeDefinition GetDeclaringTypeFromField(Il2CppFieldDefinition fieldDefinition)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return null!;

        return ctx.ReflectionCache.GetDeclaringTypeFromField(ctx.Metadata, fieldDefinition);
    }

    public static Il2CppType? GetTypeFromDefinition(Il2CppTypeDefinition definition)
    {
        var ctx = LibCpp2IlMain.DefaultContext;
        if (ctx == null) return null;

        return ctx.ReflectionCache.GetTypeFromDefinition(ctx.Binary, definition);
    }
}
