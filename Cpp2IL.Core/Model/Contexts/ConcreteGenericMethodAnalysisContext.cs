using System;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly AssemblyAnalysisContext DeclaringAsm;
    public readonly Cpp2IlMethodRef? MethodRef;
    public readonly MethodAnalysisContext BaseMethodContext;

    /// <summary>
    /// The generic parameters for the <see cref="BaseMethodContext"/> declaring type.
    /// </summary>
    /// <remarks>
    /// If not empty, <see cref="MethodAnalysisContext.DeclaringType"/> is a <see cref="GenericInstanceTypeAnalysisContext"/>.
    /// </remarks>
    public TypeAnalysisContext[] TypeGenericParameters { get; }

    /// <summary>
    /// The generic parameters for the <see cref="BaseMethodContext"/>.
    /// </summary>
    /// <remarks>
    /// These may be empty if <see cref="BaseMethodContext"/> has no generic parameters or if <see cref="IsPartialInstantiation"/>.
    /// </remarks>
    public TypeAnalysisContext[] MethodGenericParameters { get; }

    /// <summary>
    /// If true, this is a generic method on a <see cref="GenericInstanceTypeAnalysisContext"/>, but it does not specify any <see cref="MethodGenericParameters"/>.
    /// </summary>
    public bool IsPartialInstantiation => MethodGenericParameters.Length == 0 && BaseMethodContext.GenericParameterCount > 0;

    public sealed override ulong UnderlyingPointer => MethodRef?.GenericVariantPtr ?? default;

    public override bool IsStatic => BaseMethodContext.IsStatic;

    public override bool IsVoid => BaseMethodContext.IsVoid;

    public override string DefaultName => BaseMethodContext.DefaultName;

    public override MethodAttributes Attributes => BaseMethodContext.Attributes;

    public override AssemblyAnalysisContext CustomAttributeAssembly => BaseMethodContext.CustomAttributeAssembly;

    public ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context)
        : this(methodRef, ResolveDeclaringAssembly(methodRef, context))
    {
    }

    private ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, AssemblyAnalysisContext declaringAssembly)
        : this(
              methodRef,
              ResolveBaseMethod(methodRef, declaringAssembly.GetTypeByDefinition(methodRef.DeclaringType)!),
              ResolveDeclaringType(methodRef, declaringAssembly),
              ResolveTypeArray(methodRef.TypeGenericParams, declaringAssembly),
              ResolveTypeArray(methodRef.MethodGenericParams, declaringAssembly),
              declaringAssembly)
    {
    }

    /// <summary>
    /// Generically instantiate a method.
    /// </summary>
    /// <param name="baseMethod">The method definition on which this instantiation is based.</param>
    /// <param name="typeGenericParameters">The type parameters for the declaring type, if any. These must always be specified.</param>
    /// <param name="methodGenericParameters">
    /// The type parameters for the base method, if any.
    /// These may be omitted (<see cref="IsPartialInstantiation"/> == <see langword="true"/>).
    /// </param>
    public ConcreteGenericMethodAnalysisContext(MethodAnalysisContext baseMethod, TypeAnalysisContext[] typeGenericParameters, TypeAnalysisContext[] methodGenericParameters)
        : this(
              null,
              baseMethod,
              typeGenericParameters.Length > 0 ? baseMethod.DeclaringType!.MakeGenericInstanceType(typeGenericParameters) : baseMethod.DeclaringType!,
              typeGenericParameters,
              methodGenericParameters,
              baseMethod.CustomAttributeAssembly)
    {
        if (baseMethod.DeclaringType!.GenericParameterCount != typeGenericParameters.Length)
            throw new ArgumentException("The number of type generic parameters must match the number of generic parameters on the declaring type.");

        if (methodGenericParameters.Length > 0 && baseMethod.GenericParameterCount != methodGenericParameters.Length)
            throw new ArgumentException("The number of method generic parameters must match the number of generic parameters on the base method.");
    }

    private ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef? methodRef, MethodAnalysisContext baseMethodContext, TypeAnalysisContext declaringType, TypeAnalysisContext[] typeGenericParameters, TypeAnalysisContext[] methodGenericParameters, AssemblyAnalysisContext declaringAssembly)
        : base(null, declaringType)
    {
        MethodRef = methodRef;
        DeclaringAsm = declaringAssembly;
        BaseMethodContext = baseMethodContext;

        TypeGenericParameters = typeGenericParameters;
        MethodGenericParameters = methodGenericParameters;

        // For the purpose of generic instantiation, we need an array of method generic parameters, even if none are provided.
        if (methodGenericParameters.Length == 0 && baseMethodContext.GenericParameterCount > 0)
            methodGenericParameters = Enumerable.Range(0, baseMethodContext.GenericParameterCount)
                .Select(i => new GenericParameterTypeAnalysisContext("T", i, Il2CppTypeEnum.IL2CPP_TYPE_MVAR, declaringAssembly))
                .ToArray();

        for (var i = 0; i < BaseMethodContext.Parameters.Count; i++)
        {
            var parameter = BaseMethodContext.Parameters[i];
            var parameterType = parameter.ParameterTypeContext;
            var instantiatedType = GenericInstantiation.Instantiate(
                parameter.ParameterTypeContext,
                typeGenericParameters,
                methodGenericParameters);

            Parameters.Add(parameterType == instantiatedType
                ? parameter
                : new InjectedParameterAnalysisContext(parameter.Name, instantiatedType, i, BaseMethodContext));
        }

        InjectedReturnType = GenericInstantiation.Instantiate(BaseMethodContext.ReturnTypeContext, typeGenericParameters, methodGenericParameters);

        if (UnderlyingPointer != 0)
            rawMethodBody = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
    }

    private static AssemblyAnalysisContext ResolveDeclaringAssembly(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context)
    {
        return context.GetAssemblyByName(methodRef.DeclaringType.DeclaringAssembly!.Name!)
               ?? throw new($"Unable to resolve declaring assembly {methodRef.DeclaringType.DeclaringAssembly.Name} for generic method {methodRef}");
    }

    private static TypeAnalysisContext ResolveDeclaringType(Cpp2IlMethodRef methodRef, AssemblyAnalysisContext declaringAssembly)
    {
        var baseType = declaringAssembly.AppContext.ResolveContextForType(methodRef.DeclaringType)
                       ?? throw new($"Unable to resolve declaring type {methodRef.DeclaringType.FullName} for generic method {methodRef}");

        if (methodRef.TypeGenericParams.Length == 0)
            return baseType;

        var genericParams = ResolveTypeArray(methodRef.TypeGenericParams, declaringAssembly);

        return new GenericInstanceTypeAnalysisContext(baseType, genericParams, declaringAssembly);
    }

    private static TypeAnalysisContext[] ResolveTypeArray(Il2CppTypeReflectionData[] array, AssemblyAnalysisContext declaringAssembly)
    {
        if (array.Length == 0)
            return [];

        var ret = new TypeAnalysisContext[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            ret[i] = array[i].ToContext(declaringAssembly)
                     ?? throw new($"Unable to resolve generic parameter {array[i]} for generic method.");
        }

        return ret;
    }

    private static MethodAnalysisContext ResolveBaseMethod(Cpp2IlMethodRef methodRef, TypeAnalysisContext declaringType)
    {
        return declaringType.GetMethod(methodRef.BaseMethod)
               ?? throw new($"Unable to resolve base method {methodRef.BaseMethod} for generic method {methodRef}");
    }
}
