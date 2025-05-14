using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public sealed class SentinelTypeAnalysisContext(AssemblyAnalysisContext referencedFrom) : ReferencedTypeAnalysisContext(referencedFrom)
{
    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_SENTINEL;
    public override string DefaultName => "<<SENTINEL>>";
    public override bool IsValueType => false;
}
