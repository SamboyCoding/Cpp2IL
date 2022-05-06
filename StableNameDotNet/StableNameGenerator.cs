using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using StableNameDotNet.Providers;

namespace StableNameDotNet;

public static class StableNameGenerator
{
    public static Regex? ObfuscatedNameRegex;

    public static readonly ConcurrentDictionary<ITypeInfoProvider, string> RenamedTypes = new();
    
    private static readonly string[] ClassAccessNames = {"Private", "Public", "NPublic", "NPrivate", "NProtected", "NInternal", "NFamAndAssem", "NFamOrAssem"};

    public static string? GetStableNameForTypeIfNeeded(ITypeInfoProvider type, bool includeMethodsForNonInterfaces)
    {
        if (!IsObfuscated(type.TypeName))
            return null;

        var inheritanceDepth = -1;

        ITypeInfoProvider? firstUnobfuscatedType = null;
        foreach (var baseType in type.GetBaseTypeHierarchy())
        {
            firstUnobfuscatedType = baseType;
            inheritanceDepth++;

            if (!RenamedTypes.ContainsKey(baseType) && !IsObfuscated(baseType.TypeName))
                break;
        }

        var classifier = type.TypeAttributes.HasFlag(TypeAttributes.Interface) ? "Interface" : type.IsValueType ? "Struct" : "Class";
        var unobfuscatedInterfaces = type.Interfaces.Where(i => !IsObfuscated(i.TypeName)).ToList();
        var accessName = ClassAccessNames[(int) (type.TypeAttributes & TypeAttributes.VisibilityMask)];

        var nameBuilder = new StringBuilder();

        nameBuilder.Append(firstUnobfuscatedType?.FormatTypeName().Join() ?? classifier);
        if (inheritanceDepth > 0)
            nameBuilder.Append(inheritanceDepth);

        if (type.TypeName.StartsWith("<"))
            nameBuilder.Append("CompilerGenerated");

        nameBuilder.Append(accessName);

        if (type.TypeAttributes.HasFlag(TypeAttributes.Abstract))
            nameBuilder.Append("Abstract");
        if (type.TypeAttributes.HasFlag(TypeAttributes.Sealed))
            nameBuilder.Append("Sealed");
        if (type.TypeAttributes.HasFlag(TypeAttributes.SpecialName))
            nameBuilder.Append("SpecialName");

        foreach (var interfaceType in unobfuscatedInterfaces)
            nameBuilder.Append(interfaceType.FormatTypeName().Join());

        var uniqueNameGenerator = new UniqueNameGenerator();

        var isEnum = type.IsEnumType;

        var fields = type.FieldInfoProviders.ToArray();
        foreach (var fieldInfoProvider in fields)
        {
            if (!isEnum && !uniqueNameGenerator.PushInputs(fieldInfoProvider.FieldTypeInfoProvider.FormatTypeName()))
                break;

            if (!uniqueNameGenerator.PushInput(fieldInfoProvider.FieldName))
                break;

            if (uniqueNameGenerator.IsFull)
                break;
        }

        if (isEnum)
            uniqueNameGenerator.PushInput(fields.Length + "v");

        foreach (var propertyInfoProvider in type.PropertyInfoProviders)
        {
            if (!uniqueNameGenerator.PushInputs(propertyInfoProvider.PropertyType.FormatTypeName()))
                break;

            if (!uniqueNameGenerator.PushInput(propertyInfoProvider.PropertyName))
                break;

            if (uniqueNameGenerator.IsFull)
                break;
        }

        if (firstUnobfuscatedType?.TypeName == nameof(MulticastDelegate))
        {
            var invokeMethod = type.MethodInfoProviders.FirstOrDefault(m => m.MethodName == "Invoke");
            if (invokeMethod != null)
            {
                uniqueNameGenerator.PushInputs(invokeMethod.ReturnType.FormatTypeName());

                foreach (var invokeMethodParameter in invokeMethod.ParameterInfoProviders)
                {
                    uniqueNameGenerator.PushInputs(invokeMethodParameter.ParameterTypeInfoProvider.FormatTypeName());

                    if (uniqueNameGenerator.IsFull)
                        break;
                }
            }
        }

        if (includeMethodsForNonInterfaces || type.TypeAttributes.HasFlag(TypeAttributes.Interface))
        {
            //Method ordering on interfaces is stable, so we can use this to generate unique names
            //Otherwise it's controlled by a flag
            foreach (var methodInfoProvider in type.MethodInfoProviders)
            {
                uniqueNameGenerator.PushInput(methodInfoProvider.MethodName);
                uniqueNameGenerator.PushInputs(methodInfoProvider.ReturnType.FormatTypeName());
                
                foreach (var parameterInfoProvider in methodInfoProvider.ParameterInfoProviders)
                {
                    uniqueNameGenerator.PushInput(parameterInfoProvider.ParameterName);
                    uniqueNameGenerator.PushInputs(parameterInfoProvider.ParameterTypeInfoProvider.FormatTypeName());
                    
                    if(uniqueNameGenerator.IsFull)
                        break;
                }
                
                if(uniqueNameGenerator.IsFull)
                    break;
            }
        }

        nameBuilder.Append(uniqueNameGenerator.GenerateUniqueName());

        if (type.GenericParameterCount > 0)
            nameBuilder.Append('`').Append(type.GenericParameterCount);

        return nameBuilder.ToString();
    }

    private static bool IsObfuscated(string s)
    {
        if (ObfuscatedNameRegex != null)
            return ObfuscatedNameRegex.IsMatch(s);

        return s.ContainsAnyInvalidSourceCodeChars(true);
    }

    private static List<string> FormatTypeName(this ITypeInfoProvider type)
    {
        if (type.IsGenericInstance)
        {
            var typeName = type.NameOrRename();
            if (typeName.IndexOf('`') is var i and > 0)
                typeName = typeName.Substring(0, i);

            var genericArgs = type.GenericArgumentInfoProviders.ToList();
            var ret = new List<string>
            {
                typeName,
                genericArgs.Count.ToString()
            };

            ret.AddRange(genericArgs.SelectMany(FormatTypeName));

            return ret;
        }

        if (IsObfuscated(type.NameOrRename()))
            return new() {"Obf"};

        return new() {type.NameOrRename()};
    }

    private static string NameOrRename(this ITypeInfoProvider typeName)
    {
        if (RenamedTypes.TryGetValue(typeName, out var rename))
            return (rename.StableHash() % (ulong) Math.Pow(10, UniqueNameGenerator.NumCharsToTakeFromEachInput)).ToString();

        return typeName.TypeName;
    }
}