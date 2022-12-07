using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.CorePlugin;

public class CallAnalysisProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Call Analyzer";
    public override string Id => "callanalyzer";
    /// <summary>
    /// We don't want 1000 attributes on a single method
    /// </summary>
    const int MaximumCalledByAttributes = 20;

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        InjectAttribute(appContext);
    }

    private static void InjectAttribute(ApplicationAnalysisContext appContext)
    {
        const string Namespace = "Cpp2ILInjected.CallAnalysis";

        var deduplicatedMethodAttributes = InjectZeroParameterAttribute(appContext, Namespace, "DeduplicatedMethodAttribute", AttributeTargets.Method, false);
        var analysisFailedAttributes = InjectZeroParameterAttribute(appContext, Namespace, "CallAnalysisFailedAttribute", AttributeTargets.Method, false);
        var analysisNotSupportedAttributes = InjectZeroParameterAttribute(appContext, Namespace, "CallAnalysisNotSupportedAttribute", AttributeTargets.Method, false);

        var callsDeduplicatedMethodsAttributes = InjectOneParameterAttribute(appContext, Namespace, "CallsDeduplicatedMethodsAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");
        var callsUnknownMethodsAttributes = InjectOneParameterAttribute(appContext, Namespace, "CallsUnknownMethodsAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");
        var callerCountAttributes = InjectOneParameterAttribute(appContext, Namespace, "CallerCountAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");

        var callsAttributes = InjectTwoParameterAttribute(appContext, Namespace, "CallsAttribute", AttributeTargets.Method, true, appContext.SystemTypes.SystemTypeType, "Type", appContext.SystemTypes.SystemStringType, "Member");
        var calledByAttributes = InjectTwoParameterAttribute(appContext, Namespace, "CalledByAttribute", AttributeTargets.Method, true, appContext.SystemTypes.SystemTypeType, "Type", appContext.SystemTypes.SystemStringType, "Member");

        Dictionary<ulong, int> callCounts = new();
        Dictionary<MethodAnalysisContext, int> unknownCalls = new();
        Dictionary<MethodAnalysisContext, int> deduplicatedCalls = new();
        Dictionary<MethodAnalysisContext, List<MethodAnalysisContext>> callsDictionary = new();
        Dictionary<MethodAnalysisContext, List<MethodAnalysisContext>> calledByDictionary = new();

        var keyFunctionAddresses = appContext.GetOrCreateKeyFunctionAddresses();

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var analysisFailedConstructor = analysisFailedAttributes[assemblyAnalysisContext];
            var analysisNotSupportedConstructor = analysisNotSupportedAttributes[assemblyAnalysisContext];
            var deduplicatedMethodConstructor = deduplicatedMethodAttributes[assemblyAnalysisContext];

            foreach (var m in assemblyAnalysisContext.Types.SelectMany(t => t.Methods))
            {
                m.AnalyzeCustomAttributeData();
                if (m.CustomAttributes == null || m.Definition == null)
                    continue;

                if (appContext.MethodsByAddress.TryGetValue(m.UnderlyingPointer, out var methodsWithThatAddress) && methodsWithThatAddress.Count > 1)
                {
                    AddZeroParameterAttribute(m, deduplicatedMethodConstructor);
                }
                
                try
                {
                    m.Analyze();
                }
                catch
                {
                    AddZeroParameterAttribute(m, analysisFailedConstructor);
                    continue;
                }

                if (m.ConvertedIsil is null or { Count: 0 })
                {
                    if ((m.MethodAttributes & MethodAttributes.Abstract) == 0)
                    {
                        AddZeroParameterAttribute(m, analysisNotSupportedConstructor);
                    }
                    continue;
                }

                foreach (var instruction in m.ConvertedIsil)
                {
                    if (instruction.OpCode != InstructionSetIndependentOpCode.Call && instruction.OpCode != InstructionSetIndependentOpCode.CallNoReturn)
                    {
                        continue;
                    }
                    if (instruction.Operands.Length > 0 && instruction.Operands[0].Data is IsilImmediateOperand operand)
                    {
                        var address = operand.Value.ToUInt64(null);
                        if (appContext.MethodsByAddress.TryGetValue(address, out var list))
                        {
                            IncreaseCount(callCounts, address);
                            if (list.Count == 0)
                            {
                                IncreaseCount(unknownCalls, m);
                            }
                            else if (list.Count > 1)
                            {
                                IncreaseCount(deduplicatedCalls, m);
                            }
                            else
                            {
                                var calledMethod = list[0];
                                Add(callsDictionary, m, calledMethod);
                                Add(calledByDictionary, calledMethod, m);
                            }
                        }
                        else if (!keyFunctionAddresses.IsKeyFunctionAddress(address))
                        {
                            IncreaseCount(unknownCalls, m);
                        }
                    }
                    else
                    {
                        IncreaseCount(unknownCalls, m);
                    }
                }
            }
        }

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var callerCountAttributeInfo = callerCountAttributes[assemblyAnalysisContext];
            var callsUnknownMethodsAttributeInfo = callsUnknownMethodsAttributes[assemblyAnalysisContext];
            var callsDeduplicatedMethodsAttributeInfo = callsDeduplicatedMethodsAttributes[assemblyAnalysisContext];
            var callsAttributeInfo = callsAttributes[assemblyAnalysisContext];
            var calledByAttributeInfo = calledByAttributes[assemblyAnalysisContext];

            foreach (var m in assemblyAnalysisContext.Types.SelectMany(t => t.Methods))
            {
                if (m.CustomAttributes == null || m.Definition == null)
                    continue;

                AddOneParameterAttribute(m, callerCountAttributeInfo, GetCount(callCounts, m.UnderlyingPointer));
                var unknownCallCount = GetCount(unknownCalls, m);
                if (deduplicatedCalls.TryGetValue(m, out var deduplicatedCallCount))
                {
                    AddOneParameterAttribute(m, callsDeduplicatedMethodsAttributeInfo, deduplicatedCallCount);
                }
                if (callsDictionary.TryGetValue(m, out var callsList))
                {
                    foreach (var calledMethod in callsList)
                    {
                        var il2cppType = GetTypeFromContext(calledMethod.DeclaringType);
                        if (il2cppType == null)
                        {
                            unknownCallCount++;
                        }
                        else
                        {
                            AddTwoParameterAttribute(m, callsAttributeInfo, il2cppType, calledMethod.Name);
                        }
                    }
                }
                if (calledByDictionary.TryGetValue(m, out var calledByList) && calledByList.Count < MaximumCalledByAttributes)
                {
                    foreach (var callingMethod in calledByList)
                    {
                        var il2cppType = GetTypeFromContext(callingMethod.DeclaringType);
                        if (il2cppType == null)
                        {
                            //If null, nothing we can do
                            continue;
                        }
                        AddTwoParameterAttribute(m, calledByAttributeInfo, il2cppType, callingMethod.Name);
                    }
                }
                if (unknownCallCount > 0)
                {
                    AddOneParameterAttribute(m, callsUnknownMethodsAttributeInfo, unknownCallCount);
                }
            }
        }
    }

    /// <summary>
    /// Inject an attribute with no fields nor properties into the <see cref="ApplicationAnalysisContext"/>.
    /// </summary>
    /// <returns>A dictionary of assembly contexts to their inject attribute constructors.</returns>
    private static Dictionary<AssemblyAnalysisContext, InjectedMethodAnalysisContext> InjectZeroParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple)
    {
        var multiInjectType = appContext.InjectTypeIntoAllAssemblies(ns, name, appContext.SystemTypes.SystemAttributeType);
        ApplyAttributeUsageAttribute(appContext, multiInjectType, attributeTargets, allowMultiple);
        return multiInjectType.InjectConstructor(false);
    }

    private static Dictionary<AssemblyAnalysisContext, (InjectedMethodAnalysisContext, InjectedFieldAnalysisContext)> InjectOneParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple, TypeAnalysisContext fieldType, string fieldName)
    {
        var multiInjectType = appContext.InjectTypeIntoAllAssemblies(ns, name, appContext.SystemTypes.SystemAttributeType);
        ApplyAttributeUsageAttribute(appContext, multiInjectType, attributeTargets, allowMultiple);

        var fields = multiInjectType.InjectFieldToAllAssemblies(fieldName, fieldType, FieldAttributes.Public);

        var constructors = multiInjectType.InjectConstructor(false);

        return multiInjectType.InjectedTypes.ToDictionary(t => t.DeclaringAssembly, t => (constructors[t.DeclaringAssembly], fields[t.DeclaringAssembly]));
    }

    private static Dictionary<AssemblyAnalysisContext, (InjectedMethodAnalysisContext, InjectedFieldAnalysisContext, InjectedFieldAnalysisContext)> InjectTwoParameterAttribute(ApplicationAnalysisContext appContext, string ns, string name, AttributeTargets attributeTargets, bool allowMultiple, TypeAnalysisContext fieldType1, string fieldName1, TypeAnalysisContext fieldType2, string fieldName2)
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

    private static void AddZeroParameterAttribute(HasCustomAttributes customAttributeHolder, MethodAnalysisContext constructor)
    {
        var newAttribute = new AnalyzedCustomAttribute(constructor);
        customAttributeHolder.CustomAttributes!.Add(newAttribute);//Nullability checked elsewhere
    }

    private static void AddOneParameterAttribute(HasCustomAttributes customAttributeHolder, (MethodAnalysisContext, FieldAnalysisContext) attributeInfo, object fieldValue)
    {
        AddOneParameterAttribute(customAttributeHolder, attributeInfo.Item1, attributeInfo.Item2, fieldValue);
    }

    private static void AddOneParameterAttribute(HasCustomAttributes customAttributeHolder, MethodAnalysisContext constructor, FieldAnalysisContext field, object fieldValue)
    {
        var newAttribute = new AnalyzedCustomAttribute(constructor);
        newAttribute.Fields.Add(new(field, MakeFieldParameter(fieldValue, newAttribute, 0)));
        customAttributeHolder.CustomAttributes!.Add(newAttribute);//Nullability checked elsewhere
    }

    private static void AddTwoParameterAttribute(HasCustomAttributes customAttributeHolder, (MethodAnalysisContext, FieldAnalysisContext, FieldAnalysisContext) attributeInfo, object fieldValue1, object fieldValue2)
    {
        AddTwoParameterAttribute(customAttributeHolder, attributeInfo.Item1, attributeInfo.Item2, fieldValue1, attributeInfo.Item3, fieldValue2);
    }

    private static void AddTwoParameterAttribute(HasCustomAttributes customAttributeHolder, MethodAnalysisContext constructor, FieldAnalysisContext field1, object fieldValue1, FieldAnalysisContext field2, object fieldValue2)
    {
        var newAttribute = new AnalyzedCustomAttribute(constructor);
        newAttribute.Fields.Add(new(field1, MakeFieldParameter(fieldValue1, newAttribute, 0)));
        newAttribute.Fields.Add(new(field2, MakeFieldParameter(fieldValue2, newAttribute, 1)));
        customAttributeHolder.CustomAttributes!.Add(newAttribute);//Nullability checked elsewhere
    }

    private static void ApplyAttributeUsageAttribute(ApplicationAnalysisContext appContext, MultiAssemblyInjectedType multiAssemblyInjectedType, AttributeTargets attributeTargets, bool allowMultiple)
    {
        var mscorlibAssembly = appContext.GetAssemblyByName("mscorlib") ?? throw new("Could not find mscorlib");
        var targetsEnum = mscorlibAssembly.GetTypeByFullName($"System.{nameof(AttributeTargets)}") ?? throw new("Could not find AttributeTargets");
        var targetsEnumType = GetTypeFromContext(targetsEnum) ?? throw new("Could not get the Il2CppType for AttributeTargets");
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

    private static BaseCustomAttributeParameter MakeFieldParameter(object fieldValue, AnalyzedCustomAttribute owner, int index)
    {
        return fieldValue switch
        {
            Il2CppType type => new CustomAttributeTypeParameter(type, owner, CustomAttributeParameterKind.Field, index),
            IConvertible convertible => new CustomAttributePrimitiveParameter(convertible, owner, CustomAttributeParameterKind.Field, index),
            _ => throw new NotSupportedException(),
        };
    }

    private static Il2CppType? GetTypeFromContext(TypeAnalysisContext? type)
    {
        return type?.Definition is null ? null : LibCpp2IlReflection.GetTypeFromDefinition(type.Definition);
    }

    private static int GetCount<T>(Dictionary<T, int> dictionary, T item) where T : notnull
    {
        if (dictionary.TryGetValue(item, out var count))
        {
            return count;
        }
        else
        {
            return count;
        }
    }

    private static void IncreaseCount<T>(Dictionary<T, int> dictionary, T item) where T : notnull
    {
        dictionary[item] = GetCount(dictionary, item) + 1;
    }

    private static void Add<T>(Dictionary<T, List<T>> dictionary, T key, T value) where T : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = new();
            dictionary.Add(key, list);
        }
        list.Add(value);
    }
}
