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

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_PINNED;

    public override string DefaultName => ElementType.Name;

    public sealed override bool IsValueType => false;
}
