using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
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

        var deduplicatedMethodAttributes = AttributeInjectionUtils.InjectZeroParameterAttribute(appContext, Namespace, "DeduplicatedMethodAttribute", AttributeTargets.Method, false);
        var analysisFailedAttributes = AttributeInjectionUtils.InjectZeroParameterAttribute(appContext, Namespace, "CallAnalysisFailedAttribute", AttributeTargets.Method, false);
        var analysisNotSupportedAttributes = AttributeInjectionUtils.InjectZeroParameterAttribute(appContext, Namespace, "CallAnalysisNotSupportedAttribute", AttributeTargets.Method, false);

        var callsDeduplicatedMethodsAttributes = AttributeInjectionUtils.InjectOneParameterAttribute(appContext, Namespace, "CallsDeduplicatedMethodsAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");
        var callsUnknownMethodsAttributes = AttributeInjectionUtils.InjectOneParameterAttribute(appContext, Namespace, "CallsUnknownMethodsAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");
        var callerCountAttributes = AttributeInjectionUtils.InjectOneParameterAttribute(appContext, Namespace, "CallerCountAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");

        var callsAttributes = AttributeInjectionUtils.InjectThreeParameterAttribute(appContext, Namespace, "CallsAttribute", AttributeTargets.Method, true, appContext.SystemTypes.SystemTypeType, "Type", appContext.SystemTypes.SystemStringType, "TypeFullName", appContext.SystemTypes.SystemStringType, "Member");
        var calledByAttributes = AttributeInjectionUtils.InjectThreeParameterAttribute(appContext, Namespace, "CalledByAttribute", AttributeTargets.Method, true, appContext.SystemTypes.SystemTypeType, "Type", appContext.SystemTypes.SystemStringType, "TypeFullName", appContext.SystemTypes.SystemStringType, "Member");

        Dictionary<ulong, int> callCounts = new();
        Dictionary<MethodAnalysisContext, int> unknownCalls = new();
        Dictionary<MethodAnalysisContext, int> deduplicatedCalls = new();
        Dictionary<MethodAnalysisContext, HashSet<MethodAnalysisContext>> callsDictionary = new();
        Dictionary<MethodAnalysisContext, HashSet<MethodAnalysisContext>> calledByDictionary = new();

        var keyFunctionAddresses = appContext.GetOrCreateKeyFunctionAddresses();

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var analysisFailedConstructor = analysisFailedAttributes[assemblyAnalysisContext];
            var analysisNotSupportedConstructor = analysisNotSupportedAttributes[assemblyAnalysisContext];
            var deduplicatedMethodConstructor = deduplicatedMethodAttributes[assemblyAnalysisContext];

            foreach (var m in assemblyAnalysisContext.Types.SelectMany(t => t.Methods))
            {
                m.AnalyzeCustomAttributeData();
                if (m.CustomAttributes == null || m.UnderlyingPointer == 0)
                    continue;

                if (appContext.MethodsByAddress.TryGetValue(m.UnderlyingPointer, out var methodsWithThatAddress) && methodsWithThatAddress.Count > 1)
                {
                    AttributeInjectionUtils.AddZeroParameterAttribute(m, deduplicatedMethodConstructor);
                }
                
                try
                {
                    m.Analyze();
                }
                catch
                {
                    m.ConvertedIsil = null;
                    AttributeInjectionUtils.AddZeroParameterAttribute(m, analysisFailedConstructor);
                    continue;
                }

                if (m.ConvertedIsil is { Count: 0 })
                {
                    if ((m.MethodAttributes & MethodAttributes.Abstract) == 0)
                    {
                        AttributeInjectionUtils.AddZeroParameterAttribute(m, analysisNotSupportedConstructor);
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
                            callCounts[address] = callCounts.GetOrDefault(address, 0) + 1;
                            if (list.Count == 0)
                            {
                                unknownCalls[m] = unknownCalls.GetOrDefault(m, 0) + 1;
                            }
                            else if (TryGetCommonMethodFromList(list, out var calledMethod))
                            {
                                Add(callsDictionary, m, calledMethod);
                                Add(calledByDictionary, calledMethod, m);
                            }
                            else
                            {
                                deduplicatedCalls[m] = deduplicatedCalls.GetOrDefault(m, 0) + 1;
                            }
                        }
                        else if (!keyFunctionAddresses.IsKeyFunctionAddress(address) && !appContext.Binary.IsExportedFunction(address))
                        {
                            unknownCalls[m] = unknownCalls.GetOrDefault(m, 0) + 1;
                        }
                    }
                    else
                    {
                        unknownCalls[m] = unknownCalls.GetOrDefault(m, 0) + 1;
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
                if (m.CustomAttributes == null || m.UnderlyingPointer == 0)
                    continue;

                var unknownCallCount = unknownCalls.GetOrDefault(m, 0);
                if (calledByDictionary.TryGetValue(m, out var calledByList) && calledByList.Count < MaximumCalledByAttributes)
                {
                    foreach (var callingMethod in calledByList)
                    {
                        if (TryGetIl2CppTypeForMethod(m, callingMethod, out var il2cppType, out var typeFullName))
                        {
                            AttributeInjectionUtils.AddTwoParameterAttribute(m, calledByAttributeInfo.Item1, calledByAttributeInfo.Item2, il2cppType, calledByAttributeInfo.Item4, callingMethod.Name);
                        }
                        else
                        {
                            AttributeInjectionUtils.AddTwoParameterAttribute(m, calledByAttributeInfo.Item1, calledByAttributeInfo.Item3, typeFullName, calledByAttributeInfo.Item4, callingMethod.Name);
                        }
                    }
                }

                AttributeInjectionUtils.AddOneParameterAttribute(m, callerCountAttributeInfo, callCounts.GetOrDefault(m.UnderlyingPointer, 0));
                if (callsDictionary.TryGetValue(m, out var callsList))
                {
                    foreach (var calledMethod in callsList)
                    {
                        if (TryGetIl2CppTypeForMethod(m, calledMethod, out var il2cppType, out var typeFullName))
                        {
                            AttributeInjectionUtils.AddTwoParameterAttribute(m, callsAttributeInfo.Item1, callsAttributeInfo.Item2, il2cppType, callsAttributeInfo.Item4, calledMethod.Name);
                        }
                        else
                        {
                            AttributeInjectionUtils.AddTwoParameterAttribute(m, callsAttributeInfo.Item1, callsAttributeInfo.Item3, typeFullName, callsAttributeInfo.Item4, calledMethod.Name);
                        }
                    }
                }
                if (deduplicatedCalls.TryGetValue(m, out var deduplicatedCallCount))
                {
                    AttributeInjectionUtils.AddOneParameterAttribute(m, callsDeduplicatedMethodsAttributeInfo, deduplicatedCallCount);
                }
                if (unknownCallCount > 0)
                {
                    AttributeInjectionUtils.AddOneParameterAttribute(m, callsUnknownMethodsAttributeInfo, unknownCallCount);
                }
            }
        }
    }

    private static bool TryGetCommonMethodFromList(List<MethodAnalysisContext> methods, [NotNullWhen(true)] out MethodAnalysisContext? commonMethod)
    {
        if (methods.Count < 1)
        {
            throw new ArgumentException("Count cannot be 0.", nameof(methods));
        }

        var firstMethod = GetBaseMethodIfConcrete(methods[0]);

        for (var i = 1; i < methods.Count; i++)
        {
            var method = GetBaseMethodIfConcrete(methods[i]);
            if (firstMethod != method)
            {
                commonMethod = null;
                return false;
            }
        }

        commonMethod = firstMethod;
        return true;

        // Concrete generic methods have no DeclaringType, so we get the base method instead
        static MethodAnalysisContext GetBaseMethodIfConcrete(MethodAnalysisContext method)
        {
            return method is ConcreteGenericMethodAnalysisContext genericMethod ? genericMethod.BaseMethodContext : method;
        }
    }

    private static bool TryGetIl2CppTypeForMethod(MethodAnalysisContext annotedMethod, MethodAnalysisContext targetMethod, [NotNullWhen(true)] out Il2CppType? targetDeclaringType, [NotNullWhen(false)] out string? targetDeclaringTypeFullName)
    {
        var declaringType = targetMethod.DeclaringType;
        if (declaringType == null)
        {
            targetDeclaringType = null;
            targetDeclaringTypeFullName = "";
            return false;
        }
        var il2CppType = GetTypeFromContext(declaringType);
        if (il2CppType != null && annotedMethod.DeclaringType != null && declaringType.IsAccessibleTo(annotedMethod.DeclaringType))
        {
            targetDeclaringType = il2CppType;
            targetDeclaringTypeFullName = null;
            return true;
        }
        else
        {
            targetDeclaringType = null;
            targetDeclaringTypeFullName = declaringType.FullName;
            return false;
        }
    }

    private static Il2CppType? GetTypeFromContext(TypeAnalysisContext? type)
    {
        return type?.Definition is null ? null : LibCpp2IlReflection.GetTypeFromDefinition(type.Definition);
    }

    private static void Add<T>(Dictionary<T, HashSet<T>> dictionary, T key, T value) where T : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = new();
            dictionary.Add(key, list);
        }
        list.Add(value);
    }
}
