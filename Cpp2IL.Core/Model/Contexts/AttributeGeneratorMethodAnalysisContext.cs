using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class AttributeGeneratorMethodAnalysisContext(ulong pointer, ApplicationAnalysisContext context, HasCustomAttributes associatedMember) 
    : MethodAnalysisContext(context)
{
    public override ulong UnderlyingPointer { get; } = pointer;

    protected override bool IsInjected => true;
    public override string DefaultName => "<AttributeGenerator>";
    public override MethodAttributes DefaultAttributes => MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig;
    public override MethodImplAttributes DefaultImplAttributes => MethodImplAttributes.Managed;
    public override TypeAnalysisContext DefaultReturnType => AppContext.SystemTypes.SystemVoidType;
    protected override int CustomAttributeIndex => -1;

    public readonly HasCustomAttributes AssociatedMember = associatedMember;
}
