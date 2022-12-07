using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures.Types;

namespace Cpp2IL.Core.Utils.AsmResolver;

internal static class CilInstructionCollectionExtensions
{
    public static CilLocalVariable AddLocalVariable(this CilInstructionCollection instructions, TypeSignature variableType)
    {
        var variable = new CilLocalVariable(variableType);
        instructions.Owner.LocalVariables.Add(variable);
        return variable;
    }
}
