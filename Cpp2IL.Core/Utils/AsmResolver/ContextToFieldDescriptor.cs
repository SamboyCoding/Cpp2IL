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

    private static FieldSignature ToFieldSignature(this FieldAnalysisContext context, ModuleDefinition parentModule)
    {
        return new FieldSignature(context.ToTypeSignature(parentModule));
    }

    public static IFieldDescriptor ToFieldDescriptor(this FieldAnalysisContext context, ModuleDefinition parentModule)
    {
        return context is ConcreteGenericFieldAnalysisContext concreteField
            ? concreteField.ToFieldDescriptor(parentModule)
            : parentModule.DefaultImporter.ImportField(context.GetFieldDefinition());
    }

    public static IFieldDescriptor ToFieldDescriptor(this ConcreteGenericFieldAnalysisContext context, ModuleDefinition parentModule)
    {
        return new MemberReference(
            context.DeclaringType.ToTypeSignature(parentModule).ToTypeDefOrRef(),
            context.Name,
            context.BaseFieldContext.ToFieldSignature(parentModule));
    }
}
