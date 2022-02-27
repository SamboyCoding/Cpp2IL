namespace Cpp2IL.Core.Model.Contexts;

public class AttributeGeneratorMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    public override bool IsVoid => true;

    public AttributeGeneratorMethodAnalysisContext(ulong pointer, ApplicationAnalysisContext context) : base(context)
    {
        UnderlyingPointer = pointer;
        RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, true);
    }
}