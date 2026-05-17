using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class PinnedTypeAnalysisContext(TypeAnalysisContext elementType, AssemblyAnalysisContext referencedFrom)
    : WrappedTypeAnalysisContext(elementType, referencedFrom)
{
    public PinnedTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
        : this(referencedFrom.ResolveIl2CppType(rawType.GetEncapsulatedType()), referencedFrom)
    {
    }

    public sealed override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_PINNED;

    public sealed override string DefaultName => ElementType.DefaultName;

    public sealed override string? OverrideName
    {
        get => ElementType.OverrideName;
        set => ElementType.OverrideName = value;
    }

    public sealed override bool IsValueType => false;
}
