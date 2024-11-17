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

        var genericParameterCount = context.Definition?.GenericContainer?.genericParameterCount ?? 0;

        return context.IsStatic
            ? MethodSignature.CreateStatic(returnType, genericParameterCount, parameters)
            : MethodSignature.CreateInstance(returnType, genericParameterCount, parameters);
    }

    public static IMethodDescriptor ToMethodDescriptor(this MethodAnalysisContext context, ModuleDefinition parentModule)
    {
        if (context is ConcreteGenericMethodAnalysisContext concreteMethod)
        {
            var memberReference = new MemberReference(
                concreteMethod.DeclaringType?.ToTypeSignature(parentModule).ToTypeDefOrRef(),
                concreteMethod.Name,
                concreteMethod.BaseMethodContext.ToMethodSignature(parentModule));

            var methodGenericParameters = concreteMethod.ResolveMethodGenericParameters();
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
        else
        {
            return parentModule.DefaultImporter.ImportMethod(context.GetMethodDefinition());
        }
    }
}
