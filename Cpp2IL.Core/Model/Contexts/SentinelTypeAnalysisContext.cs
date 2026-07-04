using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public sealed class SentinelTypeAnalysisContext(ApplicationAnalysisContext appContext) : ReferencedTypeAnalysisContext(appContext.SystemTypes.SystemObjectType.DeclaringAssembly)
{
    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_SENTINEL;
    public override string DefaultName => "<<SENTINEL>>";
    public override string? OverrideName
    {
        get => null;
        set
        {
        }
    }
    public override string DefaultNamespace => "";
    public override string? OverrideNamespace
    {
        get => null;
        set
        {
        }
    }
    public override bool IsValueType => false;
}
