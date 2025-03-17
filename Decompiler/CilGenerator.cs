using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using Decompiler.IL;

namespace Decompiler;

/// <summary>
/// Generates .NET's CIL from decompiler IL.
/// </summary>
public static class CilGenerator
{
    /// <summary>
    /// Generates CIL for a method, there's no return value because this sets the method body.
    /// </summary>
    /// <param name="method">The method.</param>
    public static void Generate(Method method)
    {
        var definition = method.Definition;
        var module = definition.Module!;
        var importer = module.DefaultImporter;
        var corLibTypes = module.CorLibTypeFactory;

        definition.CilMethodBody = new CilMethodBody(definition);
        var cil = definition.CilMethodBody.Instructions;

        var locals = new Dictionary<LocalVariable, CilLocalVariable>();

        foreach (var local in method.Locals)
        {
            if (local.Type == null)
                locals[local] = new CilLocalVariable(corLibTypes.Object);
            else
                locals[local] = new CilLocalVariable(local.Type.ImportWith(importer));
        }

        foreach (var local in locals)
        {
            if (!method.ParameterLocals.Contains(local.Key))
                definition.CilMethodBody.LocalVariables.Add(local.Value);
        }

        foreach (var instruction in method.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Unknown:
                case OpCode.Nop:
                case OpCode.Move:
                case OpCode.LoadAddress:
                case OpCode.Phi:
                case OpCode.Call:
                case OpCode.CallVoid:
                case OpCode.TailCall:
                case OpCode.TailCallVoid:
                case OpCode.Return:
                case OpCode.ReturnVoid:
                case OpCode.Jump:
                case OpCode.ConditionalJump:
                case OpCode.ShiftStack:
                case OpCode.Add:
                case OpCode.Subtract:
                case OpCode.Multiply:
                case OpCode.Divide:
                case OpCode.ShiftLeft:
                case OpCode.ShiftRight:
                case OpCode.And:
                case OpCode.Or:
                case OpCode.Xor:
                case OpCode.Not:
                case OpCode.Negate:
                case OpCode.CheckEqual:
                case OpCode.CheckGreater:
                case OpCode.CheckLess:
                    var writeLine = corLibTypes.CorLibScope
                        .CreateTypeReference("System", "Console")
                        .CreateMemberReference("WriteLine", MethodSignature.CreateStatic(
                            corLibTypes.Void, corLibTypes.String))
                        .ImportWith(importer);
                    cil.Add(CilOpCodes.Ldstr, instruction.ToString());
                    cil.Add(CilOpCodes.Call, writeLine);
                    break;
            }
        }

        if (definition.Parameters.ReturnParameter.ParameterType != corLibTypes.Void)
            cil.Add(CilOpCodes.Ldc_I4_S, 0);

        cil.Add(CilOpCodes.Ret);
    }
}
