using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils.AsmResolver;

namespace Cpp2IL.Core;

public class Ilgenerator
{
    private Dictionary<LocalVariable, CilLocalVariable> _locals = [];
    private ModuleDefinition? _module;
    private ReferenceImporter? _importer;
    private CorLibTypeFactory? _factory;
    private MemberReference? _writeLine;

    public void GenerateIl(MethodAnalysisContext context, MethodDefinition definition)
    {
        // Change branch targets to instructions
        foreach (var instruction in context.ControlFlowGraph!.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Operands.Count > 0 && instruction.Operands[0] is Block target)
            {
                if (target.Instructions.Count > 0)
                    instruction.Operands[0] = target.Instructions[^1];
            }
        }

        _module = definition.Module!;
        _importer = _module.DefaultImporter;
        _factory = _module.CorLibTypeFactory;

        _writeLine = _factory.CorLibScope
            .CreateTypeReference("System", "Console")
            .CreateMemberReference("WriteLine", MethodSignature.CreateStatic(_factory.Void, _factory.String))
            .ImportWith(_importer);

        var body = new CilMethodBody(definition)
        {
            InitializeLocals = true, // Without this ILSpy does: CompilerServices.Unsafe.SkipInit(out object obj);
            ComputeMaxStackOnBuild = false // There's stack imbalance somewhere, but this works for now
        };

        definition.CilMethodBody = body;

        foreach (var operand in context.ControlFlowGraph.Instructions.SelectMany(i => i.Operands))
        {
            LocalVariable? local = null;

            if (operand is FieldReference field)
                local = field.Local;

            if (operand is LocalVariable local2)
                local = local2;

            if (local != null && !context.Locals.Contains(local))
                context.Locals.Add(local);
        }

        // Map ISIL locals to IL
        _locals.Clear();
        foreach (var local in context.Locals)
        {
            TypeSignature ilType;

            // Use object if type couldn't be determined
            if (local.Type != null)
                ilType = local.Type.ToTypeSignature(_module);
            else
                ilType = _module.CorLibTypeFactory.Object;

            var ilLocal = new CilLocalVariable(ilType);
            body.LocalVariables.Add(ilLocal);
            _locals.Add(local, ilLocal);
        }

        /* foreach (var instruction in context.ControlFlowGraph!.Instructions)
        {
            body.Instructions.Add(CilOpCodes.Ldstr, instruction.ToString());
            body.Instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
        }
        body.Instructions.Add(CilOpCodes.Ldstr, "-------------------------------------------------------------------------");
        body.Instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!)); */

        // Generate IL
        foreach (var instruction in context.ControlFlowGraph!.Instructions) // context.ConvertedIsil is probably not up to date anymore here
            GenerateInstructions(instruction, context, definition);
    }

    private void GenerateInstructions(Instruction instruction, MethodAnalysisContext context, MethodDefinition method)
    {
        var body = method.CilMethodBody!;
        var instructions = body.Instructions;

        switch (instruction.OpCode)
        {
            case OpCode.Invalid:
                instructions.Add(CilOpCodes.Ldstr, $"Invalid instruction: {instruction}");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.NotImplemented:
                instructions.Add(CilOpCodes.Ldstr, $"Not implemented instruction: {instruction}");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.Interrupt:
            case OpCode.Nop:
                instructions.Add(CilOpCodes.Nop);
                break;

            case OpCode.Move:
                if (instruction.Operands[0] is FieldReference field) // stfld takes instance before value so LoadOperand StoreToOperand doesn't work
                {
                    var param = method.Parameters.FirstOrDefault(p => p.Name == field.Local.Name);
                    if (param != null)
                        instructions.Add(CilOpCodes.Ldarg, param);
                    else if (field.Local.IsThis)
                        instructions.Add(CilOpCodes.Ldarg_0);
                    else
                        instructions.Add(CilOpCodes.Ldloc, _locals[field.Local]);

                    LoadOperand(instruction.Operands[1], method);
                    instructions.Add(CilOpCodes.Stfld, field.Field.ToFieldDescriptor(_module!));
                    break;
                }

                LoadOperand(instruction.Operands[1], method);
                StoreToOperand(instruction.Operands[0], method);
                break;

            case OpCode.Phi:
                instructions.Add(CilOpCodes.Ldstr, $"Phi opcodes should not exist at this point in decompilation ({instruction})");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.Call:
            case OpCode.CallVoid:
                if (instruction.Operands[0] is not MethodAnalysisContext targetMethod)
                {
                    if (instruction.Operands[0] is ulong targetAddress)
                        instructions.Add(CilOpCodes.Ldstr, $"Method not found @{targetAddress:X}");
                    else // Probably key function
                        instructions.Add(CilOpCodes.Ldstr, $"Unknown call target operand: {instruction}");

                    instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                    break;
                }

                var importedMethod = _importer!.ImportMethod(targetMethod.ToMethodDescriptor(_module!));
                var resolvedMethod = importedMethod.Resolve()!;

                var thisParamIndex = instruction.OpCode == OpCode.Call ? 2 : 1;

                if (!resolvedMethod.IsStatic) // Load 'this' param
                {
                    if ((instruction.Operands.Count - 1) >= thisParamIndex)
                        LoadOperand(instruction.Operands[thisParamIndex], method);
                    else
                        instructions.Add(CilOpCodes.Ldstr, $"Non static method called without 'this' param ({instruction})");
                }

                // Load normal params
                var callParams = instruction.Operands.Skip(thisParamIndex + (resolvedMethod.IsStatic ? 0 : -1));
                foreach (var param in callParams)
                    LoadOperand(param, method);

                instructions.Add(CilOpCodes.Call, importedMethod);

                if (instruction.OpCode == OpCode.Call) // Store return value
                    StoreToOperand(instruction.Operands[1], method);

                break;

            case OpCode.IndirectCall:
                instructions.Add(CilOpCodes.Ldstr, $"Indirect calls should have been resolved before IL gen ({instruction})");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.Return:
                if (!context.IsVoid && instruction.Operands.Count == 1)
                    LoadOperand(instruction.Operands[0], method);
                instructions.Add(CilOpCodes.Ret);
                break;

            case OpCode.Jump:
            case OpCode.IndirectJump:
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
                instructions.Add(CilOpCodes.Ldstr, instruction.ToString());
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            default:
                instructions.Add(CilOpCodes.Ldstr, $"Unknown instruction: {instruction}");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;
        }
    }

    private void LoadOperand(object operand, MethodDefinition method)
    {
        var instructions = method.CilMethodBody!.Instructions;

        switch (operand)
        {
            case int i:
                instructions.Add(CilOpCodes.Ldc_I4, i);
                break;
            case float f:
                instructions.Add(CilOpCodes.Ldc_R4, f);
                break;
            case double d:
                instructions.Add(CilOpCodes.Ldc_R8, d);
                break;
            case bool b:
                instructions.Add(CilOpCodes.Ldc_I4, b ? 1 : 0);
                break;
            case string s:
                instructions.Add(CilOpCodes.Ldstr, s);
                break;
            case LocalVariable local:
                var param = method.Parameters.FirstOrDefault(p => p.Name == local.Name);
                if (param != null)
                    instructions.Add(CilOpCodes.Ldarg, param);
                else
                    instructions.Add(CilOpCodes.Ldloc, _locals[local]);
                break;
            case FieldReference field:
                instructions.Add(CilOpCodes.Ldarg_0); // TODO: Use local instead of 'this' without causing stack imbalance, i have no idea why that happens
                //instructions.Add(CilOpCodes.Ldloca, _locals[field.Local]);
                instructions.Add(CilOpCodes.Ldfld, field.Field.ToFieldDescriptor(_module!));
                break;
            default:
                instructions.Add(CilOpCodes.Ldstr, operand.ToString() ?? "[null operand]");
                break;
        }
    }

    private void StoreToOperand(object operand, MethodDefinition method)
    {
        var instructions = method.CilMethodBody!.Instructions;

        switch (operand)
        {
            case LocalVariable local:
                instructions.Add(CilOpCodes.Stloc, _locals[local]);
                break;

            case FieldReference field:
                instructions.Add(CilOpCodes.Ldarg_0);
                instructions.Add(CilOpCodes.Stfld, field.Field.ToFieldDescriptor(_module!));
                break;

            default:
                instructions.Add(CilOpCodes.Ldstr, $"Store into unknown operand: {operand}");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;
        }
    }
}
