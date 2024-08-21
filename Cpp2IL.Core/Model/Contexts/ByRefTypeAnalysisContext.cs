using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class ByRefTypeAnalysisContext(TypeAnalysisContext elementType, AssemblyAnalysisContext referencedFrom)
    : WrappedTypeAnalysisContext(elementType, referencedFrom)
{
    public ByRefTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
        : this(default(TypeAnalysisContext)!, referencedFrom)
    {
    }

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_BYREF;

    public override string DefaultName => $"{ElementType.Name}&";

    public sealed override bool IsValueType => false;

    public override TypeAnalysisContext ElementType => base.ElementType ?? throw new("TODO Support TYPE_BYREF");
}
