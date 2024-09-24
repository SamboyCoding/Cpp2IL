namespace Cpp2IL.Core.Model.Contexts;

public class AttributeGeneratorMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    public override bool IsVoid => true;

    public readonly HasCustomAttributes AssociatedMember;

    public AttributeGeneratorMethodAnalysisContext(ulong pointer, ApplicationAnalysisContext context, HasCustomAttributes associatedMember) : base(context)
    {
        UnderlyingPointer = pointer;
        AssociatedMember = associatedMember;
        rawMethodBody = AppContext.InstructionSet.GetRawBytesForMethod(this, true);
    }
}
