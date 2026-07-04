using System;
using System.Collections.Generic;
using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly Cpp2IlMethodRef? MethodRef;
    public readonly MethodAnalysisContext BaseMethodContext;

    /// <summary>
    /// The generic parameters for the <see cref="BaseMethodContext"/> declaring type.
    /// </summary>
    /// <remarks>
    /// If not empty, <see cref="MethodAnalysisContext.DeclaringType"/> is a <see cref="GenericInstanceTypeAnalysisContext"/>.
    /// </remarks>
    public IReadOnlyList<TypeAnalysisContext> TypeGenericParameters { get; }

    /// <summary>
    /// The generic parameters for the <see cref="BaseMethodContext"/>.
    /// </summary>
    /// <remarks>
    /// These may be empty if <see cref="BaseMethodContext"/> has no generic parameters or if <see cref="IsPartialInstantiation"/>.
    /// </remarks>
    public IReadOnlyList<TypeAnalysisContext> MethodGenericParameters { get; }

    /// <summary>
    /// If true, this is a generic method on a <see cref="GenericInstanceTypeAnalysisContext"/>, but it does not specify any <see cref="MethodGenericParameters"/>.
    /// </summary>
    public bool IsPartialInstantiation => MethodGenericParameters.Count == 0 && BaseMethodContext.GenericParameters.Count > 0;

    public sealed override ulong UnderlyingPointer => MethodRef?.GenericVariantPtr ?? default;

    public override string DefaultName => BaseMethodContext.DefaultName;

    public override TypeAnalysisContext DefaultReturnType { get; }

    public override string? OverrideName { get => BaseMethodContext.OverrideName; set => BaseMethodContext.OverrideName = value; }

    public override MethodAttributes DefaultAttributes => BaseMethodContext.DefaultAttributes;

    public override MethodAttributes? OverrideAttributes { get => BaseMethodContext.OverrideAttributes; set => BaseMethodContext.OverrideAttributes = value; }

    public override MethodImplAttributes DefaultImplAttributes => BaseMethodContext.DefaultImplAttributes;

    public override MethodImplAttributes? OverrideImplAttributes { get => BaseMethodContext.OverrideImplAttributes; set => BaseMethodContext.OverrideImplAttributes = value; }

    public override AssemblyAnalysisContext CustomAttributeAssembly => BaseMethodContext.CustomAttributeAssembly;

    public ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context)
        : this(methodRef, ResolveDeclaringAssembly(methodRef, context))
    {
    }

    private ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef methodRef, AssemblyAnalysisContext declaringAssembly)
        : this(
              methodRef,
              ResolveBaseMethod(methodRef, declaringAssembly.GetTypeByDefinition(methodRef.DeclaringType)!),
              ResolveDeclaringType(methodRef, declaringAssembly.AppContext),
              ResolveTypeArray(methodRef.TypeGenericParams, declaringAssembly.AppContext),
              ResolveTypeArray(methodRef.MethodGenericParams, declaringAssembly.AppContext))
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
    public ConcreteGenericMethodAnalysisContext(MethodAnalysisContext baseMethod, IEnumerable<TypeAnalysisContext> typeGenericParameters, IEnumerable<TypeAnalysisContext> methodGenericParameters)
        : this(baseMethod, [.. typeGenericParameters], [.. methodGenericParameters])
    {
    }

    private ConcreteGenericMethodAnalysisContext(MethodAnalysisContext baseMethod, TypeAnalysisContext[] typeGenericParameters, TypeAnalysisContext[] methodGenericParameters)
        : this(
              null,
              baseMethod,
              typeGenericParameters.Length > 0 ? baseMethod.DeclaringType!.MakeGenericInstanceType(typeGenericParameters) : baseMethod.DeclaringType!,
              typeGenericParameters,
              methodGenericParameters)
    {
        if (baseMethod.DeclaringType!.GenericParameters.Count != typeGenericParameters.Length)
            throw new ArgumentException("The number of type generic parameters must match the number of generic parameters on the declaring type.");

        if (methodGenericParameters.Length > 0 && baseMethod.GenericParameters.Count != methodGenericParameters.Length)
            throw new ArgumentException("The number of method generic parameters must match the number of generic parameters on the base method.");
    }

    private ConcreteGenericMethodAnalysisContext(Cpp2IlMethodRef? methodRef, MethodAnalysisContext baseMethodContext, TypeAnalysisContext declaringType, TypeAnalysisContext[] typeGenericParameters, TypeAnalysisContext[] methodGenericParameters)
        : base(null, declaringType)
    {
        MethodRef = methodRef;
        BaseMethodContext = baseMethodContext;

        TypeGenericParameters = typeGenericParameters;
        MethodGenericParameters = methodGenericParameters;

        // For the purpose of generic instantiation, we need an array of method generic parameters, even if none are provided.
        if (methodGenericParameters.Length == 0 && baseMethodContext.GenericParameters.Count > 0)
            methodGenericParameters = baseMethodContext.GenericParameters.ToArray();

        for (var i = 0; i < BaseMethodContext.Parameters.Count; i++)
        {
            var parameter = BaseMethodContext.Parameters[i];
            var instantiatedType = GenericInstantiation.Instantiate(
                parameter.ParameterType,
                typeGenericParameters,
                methodGenericParameters);

            Parameters.Add(new ConcreteGenericParameterAnalysisContext(parameter, instantiatedType, this));
        }

        DefaultReturnType = GenericInstantiation.Instantiate(BaseMethodContext.ReturnType, typeGenericParameters, methodGenericParameters);
    }

    private static AssemblyAnalysisContext ResolveDeclaringAssembly(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext context)
    {
        return context.ResolveContextForAssembly(methodRef.DeclaringType.DeclaringAssembly)
               ?? throw new($"Unable to resolve declaring assembly {methodRef.DeclaringType.DeclaringAssembly?.Name} for generic method {methodRef}");
    }

    private static TypeAnalysisContext ResolveDeclaringType(Cpp2IlMethodRef methodRef, ApplicationAnalysisContext appContext)
    {
        var baseType = appContext.ResolveContextForType(methodRef.DeclaringType)
                       ?? throw new($"Unable to resolve declaring type {methodRef.DeclaringType.FullName} for generic method {methodRef}");

        if (methodRef.TypeGenericParams.Length == 0)
            return baseType;

        var genericParams = ResolveTypeArray(methodRef.TypeGenericParams, appContext);

        return new GenericInstanceTypeAnalysisContext(baseType, genericParams);
    }

    private static TypeAnalysisContext[] ResolveTypeArray(Il2CppTypeReflectionData[] array, ApplicationAnalysisContext appContext)
    {
        if (array.Length == 0)
            return [];

        var ret = new TypeAnalysisContext[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            ret[i] = array[i].ToContext(appContext)
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
