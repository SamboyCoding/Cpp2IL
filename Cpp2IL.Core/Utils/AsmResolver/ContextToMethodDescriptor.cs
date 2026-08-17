using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Utils.AsmResolver;

public static class ContextToMethodDescriptor
{
    private static MethodDefinition GetMethodDefinition(this MethodAnalysisContext context)
    {
        return context.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new($"AsmResolver method not found in method analysis context for {context}");
    }

    private static MethodSignature ToMethodSignature(this MethodAnalysisContext context)
    {
        var returnType = context.ReturnType.ToTypeSignature();
        var parameters = context.Parameters.Select(p => p.ToTypeSignature());

        return context.IsStatic
            ? MethodSignature.CreateStatic(returnType, context.GenericParameters.Count, parameters)
            : MethodSignature.CreateInstance(returnType, context.GenericParameters.Count, parameters);
    }

    public static IMethodDescriptor ToMethodDescriptor(this MethodAnalysisContext context)
    {
        return context is ConcreteGenericMethodAnalysisContext concreteMethod
            ? concreteMethod.ToMethodDescriptor()
            : context.GetMethodDefinition();
    }

    public static IMethodDescriptor ToMethodDescriptor(this ConcreteGenericMethodAnalysisContext context)
    {
        var memberReference = new MemberReference(
            context.DeclaringType?.ToTypeSignature().ToTypeDefOrRef(),
            context.Name,
            context.BaseMethodContext.ToMethodSignature());

        var methodGenericParameters = context.MethodGenericParameters;
        if (methodGenericParameters.Count == 0)
            return memberReference;

        var typeSignatures = methodGenericParameters.Select(p => p.ToTypeSignature());
        return memberReference.MakeGenericInstanceMethod(typeSignatures);
    }
}
