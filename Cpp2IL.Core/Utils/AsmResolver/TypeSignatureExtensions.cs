using AsmResolver.DotNet.Signatures.Types;

namespace Cpp2IL.Core.Utils.AsmResolver;

internal static class TypeSignatureExtensions
{
    public static bool IsValueTypeOrGenericParameter(this TypeSignature type)
    {
        return type is { IsValueType: true } or GenericParameterSignature;
    }
}
