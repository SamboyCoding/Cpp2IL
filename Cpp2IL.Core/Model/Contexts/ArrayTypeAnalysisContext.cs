using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class ArrayTypeAnalysisContext(TypeAnalysisContext elementType, int rank, AssemblyAnalysisContext referencedFrom)
    : WrappedTypeAnalysisContext(elementType, referencedFrom)
{
    public ArrayTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
        : this(referencedFrom.ResolveIl2CppType(rawType.GetArrayElementType()), rawType.GetArrayRank(), referencedFrom)
    {
    }

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_ARRAY;

    public override string DefaultName => $"{ElementType.Name}[{Rank}]";

    public sealed override bool IsValueType => false;

    public int Rank { get; } = rank;
}
