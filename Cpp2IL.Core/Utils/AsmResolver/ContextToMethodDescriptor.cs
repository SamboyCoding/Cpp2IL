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

    private static MethodSignature ToMethodSignature(this MethodAnalysisContext context, ModuleDefinition parentModule)
    {
        var returnType = context.ReturnTypeContext.ToTypeSignature(parentModule);
        var parameters = context.Parameters.Select(p => p.ToTypeSignature(parentModule));

        return context.IsStatic
            ? MethodSignature.CreateStatic(returnType, context.GenericParameterCount, parameters)
            : MethodSignature.CreateInstance(returnType, context.GenericParameterCount, parameters);
    }

    public static IMethodDescriptor ToMethodDescriptor(this MethodAnalysisContext context, ModuleDefinition parentModule)
    {
        return context is ConcreteGenericMethodAnalysisContext concreteMethod
            ? concreteMethod.ToMethodDescriptor(parentModule)
            : parentModule.DefaultImporter.ImportMethod(context.GetMethodDefinition());
    }

    public static IMethodDescriptor ToMethodDescriptor(this ConcreteGenericMethodAnalysisContext context, ModuleDefinition parentModule)
    {
        var memberReference = new MemberReference(
            context.DeclaringType?.ToTypeSignature(parentModule).ToTypeDefOrRef(),
            context.Name,
            context.BaseMethodContext.ToMethodSignature(parentModule));

        var methodGenericParameters = context.ResolveMethodGenericParameters();
        if (methodGenericParameters.Length == 0)
        {
            return parentModule.DefaultImporter.ImportMethod(memberReference);
        }
        else
        {
            var typeSignatures = methodGenericParameters.Select(p => p.ToTypeSignature(parentModule)).ToArray();
            return parentModule.DefaultImporter.ImportMethod(memberReference.MakeGenericInstanceMethod(typeSignatures));
        }
    }
}
