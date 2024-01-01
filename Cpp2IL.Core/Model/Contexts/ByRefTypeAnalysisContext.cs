using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class ByRefTypeAnalysisContext : WrappedTypeAnalysisContext
{
    public ByRefTypeAnalysisContext(TypeAnalysisContext elementType, AssemblyAnalysisContext referencedFrom) : base(elementType, referencedFrom)
    {
    }

    public ByRefTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
        : this(default(TypeAnalysisContext)!, referencedFrom)
    {
    }

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_BYREF;

    public override string DefaultName => $"{ElementType.Name}&";

    public override TypeAnalysisContext ElementType => base.ElementType ?? throw new("TODO Support TYPE_BYREF");

    public override TypeSignature ToTypeSignature(ModuleDefinition parentModule)
    {
        return ElementType.ToTypeSignature(parentModule).MakeByReferenceType();
    }
}
