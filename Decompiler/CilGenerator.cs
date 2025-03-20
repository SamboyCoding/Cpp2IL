using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using Decompiler.ControlFlow;
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

        var writeLine = corLibTypes.CorLibScope
            .CreateTypeReference("System", "Console")
            .CreateMemberReference("WriteLine", MethodSignature.CreateStatic(
                corLibTypes.Void, corLibTypes.String))
            .ImportWith(importer);

        definition.CilMethodBody = new CilMethodBody(definition);
        var body = definition.CilMethodBody;
        var cil = body.Instructions;

        body.BuildFlags = CilMethodBodyBuildFlags.VerifyLabels;
        body.InitializeLocals = true;

        // Add locals
        var locals = new Dictionary<LocalVariable, CilLocalVariable>();

        foreach (var local in method.Locals)
        {
            if (local.Type == null)
                locals[local] = new CilLocalVariable(corLibTypes.Object);
            else
                locals[local] = new CilLocalVariable(local.Type.ToTypeSignature().ImportWith(importer));
        }

        foreach (var local in locals)
        {
            if (!method.ParameterLocals.Contains(local.Key))
                body.LocalVariables.Add(local.Value);
        }

        // Change branch targets from blocks to instructions
        var graph = method.ControlFlowGraph;

        graph.RemoveNops();
        graph.RemoveEmptyBlocks();
        graph.FixBranches();

        // Add nops to empty blocks
        foreach (var block in graph.Blocks)
        {
            if (block.Instructions.Count == 0)
                block.AddInstruction(new Instruction(-1, OpCode.Nop));
        }

        foreach (var instruction in graph.Blocks.SelectMany(b => b.Instructions))
        {
            if (instruction.Operands.Count > 0 && instruction.Operands[0] is Block target)
            {
                instruction.Operands[0] = target.Instructions[0];
            }
        }

        var instructionMap = new Dictionary<Instruction, List<CilInstruction>>();

        foreach (var instruction in method.Instructions)
        {
            switch (instruction.OpCode)
            {
                case OpCode.Unknown:
                    AddWarning((string)instruction.Operands[0], instruction);
                    break;

                case OpCode.Nop:
                    Add(new CilInstruction(CilOpCodes.Nop), instruction);
                    break;

                case OpCode.Move:
                    if (instruction.Operands[0] is not LocalVariable moveDest || instruction.Operands[1] is not LocalVariable moveSource)
                        break;
                    LoadOperand(moveSource, instruction);
                    Add(new CilInstruction(CilOpCodes.Stloc, GetLocal(moveDest)), instruction);
                    break;

                case OpCode.LoadAddress:
                case OpCode.Phi:
                    AddWarning(instruction.ToString(), instruction);
                    break;

                case OpCode.Call:
                case OpCode.TailCall:
                    var calledMethod = (MethodDefinition)instruction.Operands[1];

                    Add(new CilInstruction(CilOpCodes.Call, calledMethod.ImportWith(importer)), instruction);
                    Add(new CilInstruction(CilOpCodes.Stloc, GetLocal((LocalVariable)instruction.Operands[0])), instruction);
                    break;

                case OpCode.CallVoid:
                case OpCode.TailCallVoid:
                    var calledVoidMethod = (MethodDefinition)instruction.Operands[0];

                    Add(new CilInstruction(CilOpCodes.Call, calledVoidMethod.ImportWith(importer)), instruction);
                    break;

                case OpCode.Return:
                    if (instruction.Operands[0] is not LocalVariable returnValue)
                        break;
                    LoadOperand(returnValue, instruction);
                    Add(new CilInstruction(CilOpCodes.Ret), instruction);
                    break;

                case OpCode.ReturnVoid:
                    Add(new CilInstruction(CilOpCodes.Ret), instruction);
                    break;

                case OpCode.Jump:
                    Add(new CilInstruction(CilOpCodes.Br, instruction.Operands[0]), instruction);
                    break;

                case OpCode.ConditionalJump:
                    LoadOperand(instruction.Operands[1], instruction);
                    Add(new CilInstruction(CilOpCodes.Brtrue, instruction.Operands[0]), instruction);
                    break;

                case OpCode.ShiftStack:
                    AddWarning(instruction.ToString(), instruction);
                    break;

                case OpCode.Add:
                case OpCode.Subtract:
                case OpCode.Multiply:
                case OpCode.Divide:
                case OpCode.ShiftLeft:
                case OpCode.ShiftRight:
                case OpCode.And:
                case OpCode.Or:
                case OpCode.Xor:
                /*if (instruction.Operands[0] is not LocalVariable binaryOpDest)
                    continue;

                var binaryOp = instruction.OpCode switch
                {
                    OpCode.Add => CilOpCodes.Add,
                    OpCode.Subtract => CilOpCodes.Sub,
                    OpCode.Multiply => CilOpCodes.Mul,
                    OpCode.Divide => CilOpCodes.Div,
                    OpCode.ShiftLeft => CilOpCodes.Shl,
                    OpCode.ShiftRight => CilOpCodes.Shr,
                    OpCode.And => CilOpCodes.And,
                    OpCode.Or => CilOpCodes.Or,
                    OpCode.Xor => CilOpCodes.Xor
                };

                LoadOperand(instruction.Operands[1], instruction);
                LoadOperand(instruction.Operands[2], instruction);
                Add(new CilInstruction(binaryOp), instruction);
                Add(new CilInstruction(CilOpCodes.Stloc, GetLocal(binaryOpDest)), instruction);
                break;*/

                case OpCode.Not:
                case OpCode.Negate:
                case OpCode.CheckEqual:
                case OpCode.CheckGreater:
                case OpCode.CheckLess:
                    AddWarning(instruction.ToString(), instruction);
                    break;
            }
        }

        if (definition.Parameters.ReturnParameter.ParameterType != corLibTypes.Void)
        {
            cil.Add(CilOpCodes.Ldc_I4_S, 0);
        }

        cil.Add(CilOpCodes.Ret);

        // Fix branches
        for (var i = 0; i < cil.Count; i++)
        {
            var instruction = cil[i];

            if (instruction.Operand is Instruction target)
            {
                if (instructionMap.TryGetValue(target, out var instructions))
                    instruction.Operand = instructions[0].CreateLabel();
                else
                    instruction.Operand = cil[0].CreateLabel();
            }
        }

        return;

        void LoadOperand(object operand, Instruction source)
        {
            switch (operand)
            {
                case int intNum:
                    Add(new CilInstruction(CilOpCodes.Ldc_I4_S, intNum), source);
                    break;
                case long longNum:
                    Add(new CilInstruction(CilOpCodes.Ldc_I8, longNum), source);
                    break;
                case ulong ulongNum:
                    Add(new CilInstruction(CilOpCodes.Ldc_I8, ulongNum), source);
                    break;
                case string text:
                    Add(new CilInstruction(CilOpCodes.Ldstr, text), source);
                    break;
                case LocalVariable local:
                    Add(new CilInstruction(CilOpCodes.Ldloc, GetLocal(local)), source);
                    break;
                default:
                    Add(new CilInstruction(CilOpCodes.Ldstr, $"Unknown operand: {operand}"), source);
                    break;
            }
        }

        CilLocalVariable GetLocal(LocalVariable local)
        {
            if (!locals.ContainsKey(local))
            {
                if (local.Type == null)
                    locals[local] = new CilLocalVariable(corLibTypes.Object);
                else
                    locals[local] = new CilLocalVariable(local.Type.ToTypeSignature().ImportWith(importer));
            }

            return locals[local];
        }

        void AddWarning(string text, Instruction source)
        {
            Add(new CilInstruction(CilOpCodes.Ldstr, text), source);
            Add(new CilInstruction(CilOpCodes.Call, writeLine), source);
        }

        void Add(CilInstruction instruction, Instruction source)
        {
            if (!instructionMap.ContainsKey(source))
                instructionMap[source] = new List<CilInstruction>();
            instructionMap[source].Add(instruction);

            cil.Add(instruction);
        }
    }
}
