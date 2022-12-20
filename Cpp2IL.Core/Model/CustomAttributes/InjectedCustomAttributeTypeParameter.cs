using System.IO;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents an injected <see cref="BaseCustomAttributeTypeParameter"/> for a <see cref="TypeAnalysisContext"/>.
/// </summary>
public class InjectedCustomAttributeTypeParameter : BaseCustomAttributeTypeParameter
{
    public TypeAnalysisContext? Type { get; }

    public InjectedCustomAttributeTypeParameter(TypeAnalysisContext? type, AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
        Type = type;
    }

    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context) => throw new System.NotSupportedException();

    public override TypeSignature? ToTypeSignature(ModuleDefinition parentModule)
    {
        if (Type == null)
            return null;
        var typeDefinition = Type.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {Type.FullName}");
        return parentModule.DefaultImporter.ImportType(typeDefinition).ToTypeSignature();
    }
}
