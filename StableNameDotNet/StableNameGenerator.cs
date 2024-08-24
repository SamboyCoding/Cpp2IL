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

    private static readonly string[] ClassAccessNames =
    [
        "Private", "Public", "NPublic", "NPrivate", "NProtected", "NInternal", "NFamAndAssem", "NFamOrAssem"
    ];

    private static readonly string[] MemberAccessTypeLabels =
    [
        "CompilerControlled", "Private", "FamAndAssem", "Internal", "Protected", "FamOrAssem", "Public"
    ];

    private static readonly (MethodSemantics, string)[] SemanticsToCheck =
    [
        (MethodSemantics.Setter, "_set"),
        (MethodSemantics.Getter, "_get"),
        (MethodSemantics.Other, "_oth"),
        (MethodSemantics.AddOn, "_add"),
        (MethodSemantics.RemoveOn, "_rem"),
        (MethodSemantics.Fire, "_fire")
    ];

    public static string? GetStableNameForTypeIfNeeded(ITypeInfoProvider type, bool includeMethodsForNonInterfaces)
    {
        return IsObfuscated(type.OriginalTypeName) ? GetStableNameForType(type, includeMethodsForNonInterfaces) : null;
    }

    public static string GetStableNameForType(ITypeInfoProvider type, bool includeMethodsForNonInterfaces)
    {
        var inheritanceDepth = -1;

        ITypeInfoProvider? firstUnobfuscatedType = null;
        foreach (var baseType in type.GetBaseTypeHierarchy())
        {
            firstUnobfuscatedType = baseType;
            inheritanceDepth++;

            if (!RenamedTypes.ContainsKey(baseType) && !IsObfuscated(baseType.OriginalTypeName))
                break;
        }

        var classifier = type.TypeAttributes.HasFlag(TypeAttributes.Interface) ? "Interface" : type.IsValueType ? "Struct" : "Class";
        var unobfuscatedInterfaces = type.Interfaces.Where(i => !IsObfuscated(i.OriginalTypeName)).ToList();
        var accessName = ClassAccessNames[(int)(type.TypeAttributes & TypeAttributes.VisibilityMask)];

        var nameBuilder = new StringBuilder();

        nameBuilder.Append(firstUnobfuscatedType?.FormatTypeNameForType().Join() ?? classifier);
        if (inheritanceDepth > 0)
            nameBuilder.Append(inheritanceDepth);

        if (type.OriginalTypeName.StartsWith("<"))
            nameBuilder.Append("CompilerGenerated");

        nameBuilder.Append(accessName);

        if (type.TypeAttributes.HasFlag(TypeAttributes.Abstract))
            nameBuilder.Append("Abstract");
        if (type.TypeAttributes.HasFlag(TypeAttributes.Sealed))
            nameBuilder.Append("Sealed");
        if (type.TypeAttributes.HasFlag(TypeAttributes.SpecialName))
            nameBuilder.Append("SpecialName");

        foreach (var interfaceType in unobfuscatedInterfaces)
            nameBuilder.Append(interfaceType.FormatTypeNameForType().Join());

        var uniqueNameGenerator = new UniqueIdentifierGenerator();

        var isEnum = type.IsEnumType;

        var fields = type.FieldInfoProviders.ToArray();
        foreach (var fieldInfoProvider in fields)
        {
            if (!isEnum && !uniqueNameGenerator.PushInputs(fieldInfoProvider.FieldTypeInfoProvider.FormatTypeNameForType()))
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
            if (!uniqueNameGenerator.PushInputs(propertyInfoProvider.PropertyTypeInfoProvider.FormatTypeNameForType()))
                break;

            if (!uniqueNameGenerator.PushInput(propertyInfoProvider.PropertyName))
                break;

            if (uniqueNameGenerator.IsFull)
                break;
        }

        if (firstUnobfuscatedType?.OriginalTypeName == nameof(MulticastDelegate))
        {
            var invokeMethod = type.MethodInfoProviders.FirstOrDefault(m => m.MethodName == "Invoke");
            if (invokeMethod != null)
            {
                uniqueNameGenerator.PushInputs(invokeMethod.ReturnType.FormatTypeNameForType());

                foreach (var invokeMethodParameter in invokeMethod.ParameterInfoProviders)
                {
                    uniqueNameGenerator.PushInputs(invokeMethodParameter.ParameterTypeInfoProvider.FormatTypeNameForType());

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
                uniqueNameGenerator.PushInputs(methodInfoProvider.ReturnType.FormatTypeNameForType());

                foreach (var parameterInfoProvider in methodInfoProvider.ParameterInfoProviders)
                {
                    uniqueNameGenerator.PushInput(parameterInfoProvider.ParameterName);
                    uniqueNameGenerator.PushInputs(parameterInfoProvider.ParameterTypeInfoProvider.FormatTypeNameForType());

                    if (uniqueNameGenerator.IsFull)
                        break;
                }

                if (uniqueNameGenerator.IsFull)
                    break;
            }
        }

        nameBuilder.Append(uniqueNameGenerator.GenerateUniqueName());

        if (type.GenericParameterCount > 0)
            if (type.OriginalTypeName.Contains('`') || type.DeclaringTypeInfoProvider == null || type.DeclaringTypeInfoProvider?.GenericParameterCount != type.GenericParameterCount)
                nameBuilder.Append('`').Append(type.GenericParameterCount);

        return nameBuilder.ToString();
    }

    public static string? GetStableNameForMethodIfNeeded(IMethodInfoProvider method)
    {
        return IsObfuscated(method.MethodName) ? GetStableNameForMethod(method) : null;
    }

    public static string GetStableNameForMethod(IMethodInfoProvider method)
    {
        //Method_ prefix
        var nameBuilder = new StringBuilder();
        nameBuilder.Append("Method_");

        //Accessibility
        var attribs = method.MethodAttributes;
        nameBuilder.Append(MemberAccessTypeLabels[(int)(attribs & MethodAttributes.MemberAccessMask)]);

        //Any other keywords
        if (attribs.HasFlag(MethodAttributes.Abstract))
            nameBuilder.Append("_Abstract");
        if (attribs.HasFlag(MethodAttributes.Virtual))
            nameBuilder.Append("_Virtual");
        if (attribs.HasFlag(MethodAttributes.Static))
            nameBuilder.Append("_Static");
        if (attribs.HasFlag(MethodAttributes.Final))
            nameBuilder.Append("_Final");
        if (attribs.HasFlag(MethodAttributes.NewSlot))
            nameBuilder.Append("_New");

        //Semantics such as getter/setter or add/remove/fire event
        var semantics = method.MethodSemantics;
        foreach (var (semantic, str) in SemanticsToCheck)
            if (semantics.HasFlag(semantic))
                nameBuilder.Append(str);

        //Return type
        nameBuilder.Append('_');
        nameBuilder.Append(method.ReturnType.FormatTypeNameForMember());

        //Params
        foreach (var parameterInfoProvider in method.ParameterInfoProviders)
        {
            nameBuilder.Append('_');
            nameBuilder.Append(parameterInfoProvider.ParameterTypeInfoProvider.FormatTypeNameForMember());
        }

        //TODO _PDM suffix

        return nameBuilder.ToString();
    }

    public static string? GetStableNameForFieldIfNeeded(IFieldInfoProvider field)
    {
        return IsObfuscated(field.FieldName) ? GetStableNameForField(field) : null;
    }

    public static string GetStableNameForField(IFieldInfoProvider field)
    {
        //Field_ prefix
        var nameBuilder = new StringBuilder();
        nameBuilder.Append("field_");

        //Accessibility
        var attribs = field.FieldAttributes;
        nameBuilder.Append(MemberAccessTypeLabels[(int)(attribs & FieldAttributes.FieldAccessMask)]);

        //Any other keywords
        if (attribs.HasFlag(FieldAttributes.Static))
            nameBuilder.Append("_Static");

        //Type
        nameBuilder.Append('_');
        nameBuilder.Append(field.FieldTypeInfoProvider.FormatTypeNameForMember());

        return nameBuilder.ToString();
    }

    public static string? GetStableNameForPropertyIfNeeded(IPropertyInfoProvider property)
    {
        return IsObfuscated(property.PropertyName) ? GetStableNameForProperty(property) : null;
    }

    public static string GetStableNameForProperty(IPropertyInfoProvider property)
    {
        //prop_ prefix
        var nameBuilder = new StringBuilder();
        nameBuilder.Append("prop_");

        //Type
        nameBuilder.Append(property.PropertyTypeInfoProvider.FormatTypeNameForMember());

        return nameBuilder.ToString();
    }

    public static string? GetStableNameForEventIfNeeded(IEventInfoProvider evt)
    {
        return IsObfuscated(evt.EventName) ? GetStableNameForEvent(evt) : null;
    }

    public static string GetStableNameForEvent(IEventInfoProvider evt)
    {
        //event_ prefix
        var nameBuilder = new StringBuilder();
        nameBuilder.Append("event_");

        //Type
        nameBuilder.Append(evt.EventTypeInfoProvider.FormatTypeNameForMember());

        return nameBuilder.ToString();
    }

    public static bool IsObfuscated(string s)
    {
        if (ObfuscatedNameRegex != null)
            return ObfuscatedNameRegex.IsMatch(s);

        return s.ContainsAnyInvalidSourceCodeChars(true);
    }

    //Unhollower is a little inconsistent - there are two "format type name"-like methods. This is the simpler of the two.
    private static List<string> FormatTypeNameForType(this ITypeInfoProvider type)
    {
        if (type.IsGenericInstance)
        {
            var typeName = type.NameOrRename();
            if (typeName.IndexOf('`') is var i and > 0)
                typeName = typeName.Substring(0, i);

            var genericArgs = type.GenericArgumentInfoProviders.ToList();
            var ret = new List<string> { typeName, genericArgs.Count.ToString() };

            ret.AddRange(genericArgs.SelectMany(FormatTypeNameForType));

            return ret;
        }

        if (IsObfuscated(type.NameOrRename()))
            return ["Obf"];

        return [type.NameOrRename()];
    }

    //And this is the second, which takes into account byref and ptr types, as well as arrays
    private static string FormatTypeNameForMember(this ITypeInfoProvider type)
    {
        var builder = new StringBuilder();
        switch (type)
        {
            case GenericInstanceTypeInfoProviderWrapper genericInst:
                builder.Append(genericInst.ElementTypeProvider.FormatTypeNameForMember());
                foreach (var genericArgument in genericInst.GenericArgumentInfoProviders)
                {
                    builder.Append('_');
                    builder.Append(genericArgument.FormatTypeNameForMember());
                }

                break;
            case ByRefTypeInfoProviderWrapper byRef:
                builder.Append("byref_");
                builder.Append(byRef.ElementTypeProvider.FormatTypeNameForMember());
                break;
            case PointerTypeInfoProviderWrapper pointer:
                builder.Append("ptr_");
                builder.Append(pointer.ElementTypeProvider.FormatTypeNameForMember());
                break;
            default:
            {
                if (type.TypeNamespace.StartsWith("System") && type.OriginalTypeName.Contains("Array"))
                    builder.Append("ArrayOf"); //Array of what? Who knows
                else
                    builder.Append(type.RewrittenTypeName.Replace('`', '_'));

                break;
            }
        }

        return builder.ToString();
    }

    private static string NameOrRename(this ITypeInfoProvider typeName)
    {
        if (RenamedTypes.TryGetValue(typeName, out var rename))
            return (rename.StableHash() % (ulong)Math.Pow(10, UniqueIdentifierGenerator.NumCharsToTakeFromEachInput)).ToString();

        return typeName.OriginalTypeName;
    }
}
