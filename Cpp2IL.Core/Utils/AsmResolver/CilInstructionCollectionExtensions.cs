using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

namespace Cpp2IL.Core.Utils.AsmResolver;

internal static class CilInstructionCollectionExtensions
{
    public static CilLocalVariable AddLocalVariable(this CilInstructionCollection instructions, TypeSignature variableType)
    {
        var variable = new CilLocalVariable(variableType);
        instructions.Owner.LocalVariables.Add(variable);
        return variable;
    }

    public static void AddDefaultValueForType(this CilInstructionCollection instructions, TypeSignature type)
    {
        if (type is CorLibTypeSignature { IsValueType: true } corLibTypeSignature)
        {
            instructions.AddDefaultPrimitiveValue(corLibTypeSignature);
        }
        else if (type is ByReferenceTypeSignature)
        {
            instructions.AddNullRef();
        }
        else if (type.IsValueTypeOrGenericParameter())
        {
            instructions.AddDefaultValueForUnknownType(type);
        }
        else
        {
            instructions.Add(CilOpCodes.Ldnull);
        }
    }

    /// <summary>
    /// Load a null reference onto the stack.
    /// </summary>
    /// <remarks>
    /// The specified instructions come from the documentation for <see cref="Unsafe.NullRef{T}"/>.<br/>
    /// They decompile as: <code>ref *(T*)null</code>
    /// The decompilation is valid C# and is the most optimized implementation.
    /// However, it does produce a compiler warning.
    /// </remarks>
    /// <param name="instructions"></param>
    private static void AddNullRef(this CilInstructionCollection instructions)
    {
        instructions.Add(CilOpCodes.Ldc_I4_0);
        instructions.Add(CilOpCodes.Conv_U);
    }

    /// <summary>
    /// Load the default value onto the stack for an unknown type.
    /// </summary>
    /// <remarks>
    /// This is a silver bullet. It handles any type except void and by ref.
    /// </remarks>
    /// <param name="instructions"></param>
    /// <param name="type"></param>
    private static void AddDefaultValueForUnknownType(this CilInstructionCollection instructions, TypeSignature type)
    {
        Debug.Assert(type is not CorLibTypeSignature { ElementType: ElementType.Void } and not ByReferenceTypeSignature);
        var variable = instructions.AddLocalVariable(type);
        instructions.Add(CilOpCodes.Ldloca, variable);
        instructions.Add(CilOpCodes.Initobj, type.ToTypeDefOrRef());
        instructions.Add(CilOpCodes.Ldloc, variable);
    }

    private static void AddDefaultPrimitiveValue(this CilInstructionCollection instructions, CorLibTypeSignature type)
    {
        switch (type.ElementType)
        {
            case ElementType.Void:
                break;
            case ElementType.U1 or ElementType.U2 or ElementType.U4 or ElementType.I1 or ElementType.I2 or ElementType.I4 or ElementType.Boolean or ElementType.Char:
                instructions.Add(CilOpCodes.Ldc_I4_0);
                break;
            case ElementType.I8:
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_I8);
                break;
            case ElementType.U8:
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_U8);
                break;
            case ElementType.I:
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_I);
                break;
            case ElementType.U:
                instructions.Add(CilOpCodes.Ldc_I4_0);
                instructions.Add(CilOpCodes.Conv_U);
                break;
            case ElementType.R4:
                instructions.Add(CilOpCodes.Ldc_R4, 0f);
                break;
            case ElementType.R8:
                instructions.Add(CilOpCodes.Ldc_R8, 0d);
                break;
            case ElementType.Object or ElementType.String:
                instructions.Add(CilOpCodes.Ldnull);
                break;
            case ElementType.TypedByRef:
                instructions.AddDefaultValueForUnknownType(type);
                break;
            default:
                throw new ArgumentOutOfRangeException(null);
        }
    }
}
