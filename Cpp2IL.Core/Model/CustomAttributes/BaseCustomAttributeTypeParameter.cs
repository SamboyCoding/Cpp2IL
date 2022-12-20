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

    public abstract TypeSignature? ToTypeSignature(ModuleDefinition parentModule);
}
