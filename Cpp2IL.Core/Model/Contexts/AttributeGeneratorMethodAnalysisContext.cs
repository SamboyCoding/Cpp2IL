using System;

namespace Cpp2IL.Core.Model.Contexts;

public class AttributeGeneratorMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer { get; }

    protected AttributeGeneratorMethodAnalysisContext(ulong pointer, ApplicationAnalysisContext context) : base(context)
    {
        UnderlyingPointer = pointer;
        RawBytes = AppContext.InstructionSet.GetRawBytesForMethod(this, true);
    }
}