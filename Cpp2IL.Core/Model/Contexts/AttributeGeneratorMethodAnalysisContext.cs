using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class AttributeGeneratorMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    protected override bool IsInjected => true;
    public override MethodAttributes DefaultAttributes => MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig;
    public override MethodImplAttributes DefaultImplAttributes => MethodImplAttributes.Managed;
    public override TypeAnalysisContext DefaultReturnType => AppContext.SystemTypes.SystemVoidType;
    protected override int CustomAttributeIndex => -1;

    public readonly HasCustomAttributes AssociatedMember;

    public AttributeGeneratorMethodAnalysisContext(ulong pointer, ApplicationAnalysisContext context, HasCustomAttributes associatedMember) : base(context)
    {
        UnderlyingPointer = pointer;
        AssociatedMember = associatedMember;
        rawMethodBody = AppContext.InstructionSet.GetRawBytesForMethod(this, true);
    }
}
