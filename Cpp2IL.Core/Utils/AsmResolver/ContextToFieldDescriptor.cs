using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Utils.AsmResolver;

public static class ContextToFieldDescriptor
{
    private static FieldDefinition GetFieldDefinition(this FieldAnalysisContext context)
    {
        return context.GetExtraData<FieldDefinition>("AsmResolverField") ?? throw new($"AsmResolver field not found in method analysis context for {context}");
    }

    private static FieldSignature ToFieldSignature(this FieldAnalysisContext context)
    {
        return new FieldSignature(context.ToTypeSignature());
    }

    public static IFieldDescriptor ToFieldDescriptor(this FieldAnalysisContext context)
    {
        return context is ConcreteGenericFieldAnalysisContext concreteField
            ? concreteField.ToFieldDescriptor()
            : context.GetFieldDefinition();
    }

    public static IFieldDescriptor ToFieldDescriptor(this ConcreteGenericFieldAnalysisContext context)
    {
        return new MemberReference(
            context.DeclaringType.ToTypeSignature().ToTypeDefOrRef(),
            context.Name,
            context.BaseFieldContext.ToFieldSignature());
    }
}
