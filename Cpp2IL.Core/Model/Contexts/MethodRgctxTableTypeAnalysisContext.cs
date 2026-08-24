using System.Collections.Generic;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

// Synthetic type for a value holding <c>MethodInfo::rgctx_data</c>
public class MethodRgctxTableTypeAnalysisContext(MethodAnalysisContext ownerMethod, AssemblyAnalysisContext referencedFrom)
    : ReferencedTypeAnalysisContext(referencedFrom)
{
    /// <summary>The (usually inflated) generic method whose runtime generic context this is.</summary>
    public MethodAnalysisContext OwnerMethod { get; } = ownerMethod;

    // see RgctxResolver.GetOrResolveEntry, resolved slots must stay reference-stable across fixpoint passes
    internal readonly Dictionary<int, TypeAnalysisContext?> ResolvedEntries = [];

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_I;

    public override string DefaultName => $"Il2CppMethodRgctx<{OwnerMethod.FullName}>";

    public override string DefaultNamespace => "";

    public override bool IsValueType => false;
}
