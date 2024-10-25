using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class ConcreteGenericMethodAnalysisContext : MethodAnalysisContext
{
    public readonly AssemblyAnalysisContext DeclaringAsm;
    public readonly Cpp2IlMethodRef MethodRef;
    public readonly MethodAnalysisContext BaseMethodContext;

    public sealed override ulong UnderlyingPointer => MethodRef.GenericVariantPtr;

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
        : base(null, ResolveDeclaringType(methodRef, declaringAssembly))
    {
        MethodRef = methodRef;
        DeclaringAsm = declaringAssembly;
        BaseMethodContext = ResolveBaseMethod(methodRef, declaringAssembly.GetTypeByDefinition(methodRef.DeclaringType)!);

        var genericTypeParameters = ResolveTypeGenericParameters();
        var genericMethodParameters = ResolveMethodGenericParameters();

        for (var i = 0; i < BaseMethodContext.Parameters.Count; i++)
        {
            var parameter = BaseMethodContext.Parameters[i];
            var parameterType = parameter.ParameterTypeContext;
            var instantiatedType = GenericInstantiation.Instantiate(
                parameter.ParameterTypeContext,
                genericTypeParameters,
                genericMethodParameters);

            Parameters.Add(parameterType == instantiatedType
                ? parameter
                : new InjectedParameterAnalysisContext(parameter.Name, instantiatedType, i, BaseMethodContext));
        }

        InjectedReturnType = GenericInstantiation.Instantiate(BaseMethodContext.ReturnTypeContext, genericTypeParameters, genericMethodParameters);

        if (UnderlyingPointer != 0)
            rawMethodBody = AppContext.InstructionSet.GetRawBytesForMethod(this, false);
    }

    public TypeAnalysisContext[] ResolveTypeGenericParameters() => ResolveTypeArray(MethodRef.TypeGenericParams, DeclaringAsm);

    public TypeAnalysisContext[] ResolveMethodGenericParameters() => ResolveTypeArray(MethodRef.MethodGenericParams, DeclaringAsm);

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
