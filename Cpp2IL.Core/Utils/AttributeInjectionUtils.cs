using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Utils;

internal static class AttributeInjectionUtils
{
    /// <summary>
    /// Inject an attribute with no fields nor properties into the <see cref="ApplicationAnalysisContext"/>.
    /// </summary>
    /// <returns>A dictionary of assembly contexts to their inject attribute constructors.</returns>
    internal static Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext> InjectZeroParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple)
    {
        var multiInjectType = appContext.InjectTypeIntoAllAssemblies(ns, name, appContext.SystemTypes.SystemAttributeType);
        ApplyAttributeUsageAttribute(appContext, multiInjectType, attributeTargets, allowMultiple);
        return multiInjectType.InjectConstructor(false);
    }

    internal static Dictionary<AssemblyAnalysisContext, (InjectedMethodAnalysisContext, InjectedFieldAnalysisContext)> InjectOneParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple, TypeAnalysisContext fieldType, string fieldName)
    {
        var multiInjectType = appContext.InjectTypeIntoAllAssemblies(ns, name, appContext.SystemTypes.SystemAttributeType);
        ApplyAttributeUsageAttribute(appContext, multiInjectType, attributeTargets, allowMultiple);

        var fields = multiInjectType.InjectFieldToAllAssemblies(fieldName, fieldType, FieldAttributes.Public);

        var constructors = multiInjectType.InjectConstructor(false);

        return multiInjectType.InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => (constructors[t.DeclaringAssembly], fields[t.DeclaringAssembly]));
    }

    internal static Dictionary<AssemblyAnalysisContext, (InjectedMethodAnalysisContext, InjectedFieldAnalysisContext, InjectedFieldAnalysisContext)> InjectTwoParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple, TypeAnalysisContext fieldType1, string fieldName1, TypeAnalysisContext fieldType2, string fieldName2)
    {
        var multiInjectType = appContext.InjectTypeIntoAllAssemblies(ns, name, appContext.SystemTypes.SystemAttributeType);
        ApplyAttributeUsageAttribute(appContext, multiInjectType, attributeTargets, allowMultiple);

        var firstFields = multiInjectType.InjectFieldToAllAssemblies(fieldName1, fieldType1, FieldAttributes.Public);

        var secondFields = multiInjectType.InjectFieldToAllAssemblies(fieldName2, fieldType2, FieldAttributes.Public);

        var constructors = multiInjectType.InjectConstructor(false);

        return multiInjectType.InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t =>
        {
            return (constructors[t.DeclaringAssembly], firstFields[t.DeclaringAssembly], secondFields[t.DeclaringAssembly]);
        });
    }

    internal static Dictionary<AssemblyAnalysisContext, (InjectedMethodAnalysisContext, InjectedFieldAnalysisContext, InjectedFieldAnalysisContext, InjectedFieldAnalysisContext)> InjectThreeParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple, TypeAnalysisContext fieldType1, string fieldName1, TypeAnalysisContext fieldType2, string fieldName2, TypeAnalysisContext fieldType3, string fieldName3)
    {
        var multiInjectType = appContext.InjectTypeIntoAllAssemblies(ns, name, appContext.SystemTypes.SystemAttributeType);
        ApplyAttributeUsageAttribute(appContext, multiInjectType, attributeTargets, allowMultiple);

        var firstFields = multiInjectType.InjectFieldToAllAssemblies(fieldName1, fieldType1, FieldAttributes.Public);

        var secondFields = multiInjectType.InjectFieldToAllAssemblies(fieldName2, fieldType2, FieldAttributes.Public);

        var thirdFields = multiInjectType.InjectFieldToAllAssemblies(fieldName3, fieldType3, FieldAttributes.Public);

        var constructors = multiInjectType.InjectConstructor(false);

        return multiInjectType.InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t =>
        {
            return (constructors[t.DeclaringAssembly], firstFields[t.DeclaringAssembly], secondFields[t.DeclaringAssembly], thirdFields[t.DeclaringAssembly]);
        });
    }

    private static void ApplyAttributeUsageAttribute(ApplicationAnalysisContext appContext, MultiAssemblyInjectedType multiAssemblyInjectedType, AttributeTargets attributeTargets, bool allowMultiple)
    {
        var mscorlibAssembly = appContext.GetAssemblyByName("mscorlib") ?? throw new("Could not find mscorlib");
        var targetsEnumType = GetAttributeTargetsType(mscorlibAssembly);
        var usageAttribute = mscorlibAssembly.GetTypeByFullName($"System.{nameof(AttributeUsageAttribute)}") ?? throw new("Could not find AttributeUsageAttribute");
        var usageConstructor = usageAttribute.Methods.First(m => (m.MethodAttributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public && m.Name == ".ctor");
        var allowMultipleProperty = usageAttribute.Properties.First(p => p.Name == nameof(AttributeUsageAttribute.AllowMultiple));
        foreach (var injectedType in multiAssemblyInjectedType.InjectedTypes)
        {
            var newAttribute = new AnalyzedCustomAttribute(usageConstructor);
            var enumParameter = new CustomAttributeEnumParameter(targetsEnumType, appContext, newAttribute, CustomAttributeParameterKind.ConstructorParam, 0);
            enumParameter.UnderlyingPrimitiveParameter.PrimitiveValue = (int)attributeTargets;
            newAttribute.ConstructorParameters.Add(enumParameter);
            newAttribute.Properties.Add(new(allowMultipleProperty, new CustomAttributePrimitiveParameter(allowMultiple, newAttribute, CustomAttributeParameterKind.Property, 1)));
            injectedType.AnalyzeCustomAttributeData();
            injectedType.CustomAttributes!.Add(newAttribute);//Nullability checked above
        }
    }

    internal static void AddZeroParameterAttribute(HasCustomAttributes customAttributeHolder, MethodAnalysisContext constructor)
    {
        var newAttribute = new AnalyzedCustomAttribute(constructor);
        customAttributeHolder.CustomAttributes!.Add(newAttribute);//Nullability checked elsewhere
    }

    internal static void AddOneParameterAttribute(HasCustomAttributes customAttributeHolder, (MethodAnalysisContext, FieldAnalysisContext) attributeInfo, object fieldValue)
    {
        AddOneParameterAttribute(customAttributeHolder, attributeInfo.Item1, attributeInfo.Item2, fieldValue);
    }

    internal static void AddOneParameterAttribute(HasCustomAttributes customAttributeHolder, MethodAnalysisContext constructor, FieldAnalysisContext field, object fieldValue)
    {
        var newAttribute = new AnalyzedCustomAttribute(constructor);
        newAttribute.Fields.Add(new(field, MakeFieldParameter(fieldValue, newAttribute, 0)));
        customAttributeHolder.CustomAttributes!.Add(newAttribute);//Nullability checked elsewhere
    }

    internal static void AddTwoParameterAttribute(HasCustomAttributes customAttributeHolder, (MethodAnalysisContext, FieldAnalysisContext, FieldAnalysisContext) attributeInfo, object fieldValue1, object fieldValue2)
    {
        AddTwoParameterAttribute(customAttributeHolder, attributeInfo.Item1, attributeInfo.Item2, fieldValue1, attributeInfo.Item3, fieldValue2);
    }

    internal static void AddTwoParameterAttribute(HasCustomAttributes customAttributeHolder, MethodAnalysisContext constructor, FieldAnalysisContext field1, object fieldValue1, FieldAnalysisContext field2, object fieldValue2)
    {
        var newAttribute = new AnalyzedCustomAttribute(constructor);
        newAttribute.Fields.Add(new(field1, MakeFieldParameter(fieldValue1, newAttribute, 0)));
        newAttribute.Fields.Add(new(field2, MakeFieldParameter(fieldValue2, newAttribute, 1)));
        customAttributeHolder.CustomAttributes!.Add(newAttribute);//Nullability checked elsewhere
    }

    private static Il2CppType GetAttributeTargetsType(AssemblyAnalysisContext mscorlibAssembly)
    {
        var targetsEnum = mscorlibAssembly.GetTypeByFullName($"System.{nameof(AttributeTargets)}") ?? throw new("Could not find AttributeTargets");
        if (targetsEnum.Definition == null)
        {
            throw new NullReferenceException("AttributeTargets had a null Definition");
        }
        else
        {
            return LibCpp2IlReflection.GetTypeFromDefinition(targetsEnum.Definition) ?? throw new("Could not get the Il2CppType for AttributeTargets");
        }
    }

    private static BaseCustomAttributeParameter MakeFieldParameter(object fieldValue, AnalyzedCustomAttribute owner, int index)
    {
        return fieldValue switch
        {
            Il2CppType type => new CustomAttributeTypeParameter(type, owner, CustomAttributeParameterKind.Field, index),
            IConvertible convertible => new CustomAttributePrimitiveParameter(convertible, owner, CustomAttributeParameterKind.Field, index),
            _ => throw new NotSupportedException(),
        };
    }
}
