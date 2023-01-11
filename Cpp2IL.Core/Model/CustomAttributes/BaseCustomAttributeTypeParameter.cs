using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a custom attribute parameter which is a type reference (typeof(x))
/// </summary>
public abstract class BaseCustomAttributeTypeParameter : BaseCustomAttributeParameter
{
    public BaseCustomAttributeTypeParameter(AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
    }

    /// <summary>
    /// Convert the parameter to an AsmResolver <see cref="TypeSignature"/>.
    /// </summary>
    /// <param name="parentModule">The <see cref="ModuleDefinition"/> this signature is being imported into.</param>
    /// <returns>An imported <see cref="TypeSignature"/> for the <paramref name="parentModule"/>.</returns>
    public abstract TypeSignature? ToTypeSignature(ModuleDefinition parentModule);
}
