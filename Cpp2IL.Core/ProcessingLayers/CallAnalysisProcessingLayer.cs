using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.ProcessingLayers;

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
        var invalidInstructionsAttributes = AttributeInjectionUtils.InjectZeroParameterAttribute(appContext, Namespace, "ContainsInvalidInstructionsAttribute", AttributeTargets.Method, false);
        var unimplementedInstructionsAttributes = AttributeInjectionUtils.InjectZeroParameterAttribute(appContext, Namespace, "ContainsUnimplementedInstructionsAttribute", AttributeTargets.Method, false);
        var analysisNotSupportedAttributes = AttributeInjectionUtils.InjectZeroParameterAttribute(appContext, Namespace, "CallAnalysisNotSupportedAttribute", AttributeTargets.Method, false);

        var callsDeduplicatedMethodsAttributes = AttributeInjectionUtils.InjectOneParameterAttribute(appContext, Namespace, "CallsDeduplicatedMethodsAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");
        var callsUnknownMethodsAttributes = AttributeInjectionUtils.InjectOneParameterAttribute(appContext, Namespace, "CallsUnknownMethodsAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");
        var callerCountAttributes = AttributeInjectionUtils.InjectOneParameterAttribute(appContext, Namespace, "CallerCountAttribute", AttributeTargets.Method, false, appContext.SystemTypes.SystemInt32Type, "Count");

        var callsAttributes = CreateCallAttributes(appContext, Namespace, "CallsAttribute");
        var calledByAttributes = CreateCallAttributes(appContext, Namespace, "CalledByAttribute");

        Dictionary<ulong, int> callCounts = new();
        Dictionary<MethodAnalysisContext, int> unknownCalls = new();
        Dictionary<MethodAnalysisContext, int> deduplicatedCalls = new();
        Dictionary<MethodAnalysisContext, HashSet<MethodAnalysisContext>> callsDictionary = new();
        Dictionary<MethodAnalysisContext, HashSet<MethodAnalysisContext>> calledByDictionary = new();

        var keyFunctionAddresses = appContext.GetOrCreateKeyFunctionAddresses();

        foreach (var assemblyAnalysisContext in appContext.Assemblies)
        {
            var invalidInstructionsConstructor = invalidInstructionsAttributes[assemblyAnalysisContext];
            var unimplementedInstructionsConstructor = unimplementedInstructionsAttributes[assemblyAnalysisContext];
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

                var convertedIsil = appContext.InstructionSet.GetIsilFromMethod(m);

                if (convertedIsil is { Count: 0 })
                {
                    if ((m.Attributes & MethodAttributes.Abstract) == 0)
                    {
                        AttributeInjectionUtils.AddZeroParameterAttribute(m, analysisNotSupportedConstructor);
                    }

                    continue;
                }

                if (convertedIsil.Any(i => i.OpCode == InstructionSetIndependentOpCode.Invalid))
                {
                    AttributeInjectionUtils.AddZeroParameterAttribute(m, invalidInstructionsConstructor);
                }

                if (convertedIsil.Any(i => i.OpCode == InstructionSetIndependentOpCode.NotImplemented))
                {
                    AttributeInjectionUtils.AddZeroParameterAttribute(m, unimplementedInstructionsConstructor);
                }

                foreach (var instruction in convertedIsil)
                {
                    if (instruction.OpCode != InstructionSetIndependentOpCode.Call && instruction.OpCode != InstructionSetIndependentOpCode.CallNoReturn)
                    {
                        continue;
                    }

                    if (instruction.Operands.Length > 0 && instruction.Operands[0].Data is IsilImmediateOperand operand && operand.Value is not string)
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

            if (Cpp2IlApi.LowMemoryMode)
                GC.Collect();
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
                        AddAttribute(calledByAttributeInfo, m, callingMethod);
                    }
                }

                AttributeInjectionUtils.AddOneParameterAttribute(m, callerCountAttributeInfo, callCounts.GetOrDefault(m.UnderlyingPointer, 0));
                if (callsDictionary.TryGetValue(m, out var callsList))
                {
                    foreach (var calledMethod in callsList)
                    {
                        AddAttribute(callsAttributeInfo, m, calledMethod);
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

            if (Cpp2IlApi.LowMemoryMode)
                GC.Collect();
        }
    }

    private static void AddAttribute((InjectedMethodAnalysisContext, InjectedFieldAnalysisContext[]) callsAttributeInfo, MethodAnalysisContext annotatedMethod, MethodAnalysisContext targetMethod)
    {
        (FieldAnalysisContext, object) typeField;
        if (TryGetDeclaringTypeForMethod(annotatedMethod, targetMethod, out var il2cppType, out var typeFullName))
        {
            typeField = (callsAttributeInfo.Item2[0], il2cppType);
        }
        else
        {
            typeField = (callsAttributeInfo.Item2[0], typeFullName);
        }

        var memberField = (callsAttributeInfo.Item2[1], targetMethod.Name);

        (FieldAnalysisContext, object)? typeParametersField;
        if (targetMethod is ConcreteGenericMethodAnalysisContext concreteMethod)
        {
            if (concreteMethod.MethodRef.MethodGenericParams.Length > 0)
            {
                var parameters = new object?[concreteMethod.MethodRef.MethodGenericParams.Length];

                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameterType = concreteMethod.MethodRef.MethodGenericParams[i].ToContext(concreteMethod.DeclaringType!.DeclaringAssembly, i);
                    if (parameterType is not null && parameterType.IsAccessibleTo(annotatedMethod.DeclaringType!))
                    {
                        parameters[i] = parameterType;
                    }
                    else
                    {
                        parameters[i] = parameterType?.FullName;
                    }
                }

                typeParametersField = (callsAttributeInfo.Item2[2], parameters);
            }
            else
            {
                typeParametersField = null;
            }
        }
        else if (targetMethod.Definition?.GenericContainer?.genericParameterCount > 0)
        {
            var parameters = targetMethod.Definition.GenericContainer.GenericParameters.Select(p => (object?)p.Name).ToArray();
            typeParametersField = (callsAttributeInfo.Item2[2], parameters);
        }
        else
        {
            typeParametersField = null;
        }

        (FieldAnalysisContext, object)? parametersField;
        if (targetMethod.ParameterCount > 0)
        {
            var parameters = new object?[targetMethod.ParameterCount];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = targetMethod.Parameters[i].ParameterTypeContext;
                if (parameterType.IsAccessibleTo(annotatedMethod.DeclaringType!) && !parameterType.HasAnyGenericParameters())
                {
                    parameters[i] = parameterType;
                }
                else
                {
                    parameters[i] = parameterType?.FullName;
                }
            }

            parametersField = (callsAttributeInfo.Item2[3], parameters);
        }
        else
        {
            parametersField = null;
        }

        (FieldAnalysisContext, object)? returnTypeField;
        TypeAnalysisContext? returnType;
        if (targetMethod is NativeMethodAnalysisContext)
        {
            returnType = null; //Native methods don't have identifiable return types.
        }
        else if (targetMethod.InjectedReturnType is not null)
        {
            returnType = targetMethod.InjectedReturnType;
        }
        else if (targetMethod.Definition is null)
        {
            returnType = null;
        }
        else
        {
            returnType = targetMethod.ReturnTypeContext;
        }

        if (returnType is not null)
        {
            if (returnType.IsAccessibleTo(annotatedMethod.DeclaringType!) && !returnType.HasAnyGenericParameters())
            {
                returnTypeField = (callsAttributeInfo.Item2[4], returnType);
            }
            else
            {
                returnTypeField = (callsAttributeInfo.Item2[4], returnType.FullName);
            }
        }
        else
        {
            returnTypeField = null;
        }

        AttributeInjectionUtils.AddAttribute(
            annotatedMethod,
            callsAttributeInfo.Item1,
            ((IEnumerable<(FieldAnalysisContext, object)>) [typeField, memberField])
            .MaybeAppend(typeParametersField)
            .MaybeAppend(parametersField)
            .MaybeAppend(returnTypeField));
    }

    private static Dictionary<AssemblyAnalysisContext, (InjectedMethodAnalysisContext, InjectedFieldAnalysisContext[])> CreateCallAttributes(ApplicationAnalysisContext appContext, string Namespace, string methodName)
    {
        return AttributeInjectionUtils.InjectAttribute(
            appContext,
            Namespace,
            methodName,
            AttributeTargets.Method,
            true,
            (appContext.SystemTypes.SystemObjectType, "Type"),
            (appContext.SystemTypes.SystemStringType, "Member"),
            (appContext.SystemTypes.SystemObjectType.MakeSzArrayType(), "MemberTypeParameters"),
            (appContext.SystemTypes.SystemObjectType.MakeSzArrayType(), "MemberParameters"),
            (appContext.SystemTypes.SystemObjectType, "ReturnType"));
    }

    private static bool TryGetCommonMethodFromList(List<MethodAnalysisContext> methods, [NotNullWhen(true)] out MethodAnalysisContext? commonMethod)
    {
        if (methods.Count < 1)
        {
            throw new ArgumentException("Count cannot be 0.", nameof(methods));
        }

        if (methods.Count == 1)
        {
            commonMethod = methods[0];
            return true;
        }

        // We attempt to unify multiple concrete generic methods into a common base method.

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

        static MethodAnalysisContext GetBaseMethodIfConcrete(MethodAnalysisContext method)
        {
            return method is ConcreteGenericMethodAnalysisContext genericMethod ? genericMethod.BaseMethodContext : method;
        }
    }

    private static bool TryGetDeclaringTypeForMethod(MethodAnalysisContext annotedMethod, MethodAnalysisContext targetMethod, [NotNullWhen(true)] out TypeAnalysisContext? targetDeclaringType, [NotNullWhen(false)] out string? targetDeclaringTypeFullName)
    {
        var declaringType = targetMethod.DeclaringType;
        if (declaringType == null)
        {
            targetDeclaringType = null;
            targetDeclaringTypeFullName = "";
            return false;
        }
        else if (annotedMethod.DeclaringType != null && declaringType.IsAccessibleTo(annotedMethod.DeclaringType))
        {
            targetDeclaringType = declaringType;
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

    private static void Add<T>(Dictionary<T, HashSet<T>> dictionary, T key, T value) where T : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = [];
            dictionary.Add(key, list);
        }

        list.Add(value);
    }
}
