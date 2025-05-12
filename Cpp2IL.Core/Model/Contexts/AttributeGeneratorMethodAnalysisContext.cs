using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class AttributeGeneratorMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    protected override bool IsInjected => true;
    public override bool IsStatic => true;
    public override bool IsVoid => true;
    public override MethodAttributes Attributes => MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig;
    protected override int CustomAttributeIndex => -1;

    public readonly HasCustomAttributes AssociatedMember;

    public AttributeGeneratorMethodAnalysisContext(ulong pointer, ApplicationAnalysisContext context, HasCustomAttributes associatedMember) : base(context)
    {
        UnderlyingPointer = pointer;
        AssociatedMember = associatedMember;
        rawMethodBody = AppContext.InstructionSet.GetRawBytesForMethod(this, true);
    }
}
