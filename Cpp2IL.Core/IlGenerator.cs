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
            InitializeLocals = true // Without this ILSpy does: CompilerServices.Unsafe.SkipInit(out object obj);
        };

        definition.CilMethodBody = body;

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

        // Generate IL
        foreach (var instruction in context.ControlFlowGraph!.Instructions) // context.ConvertedIsil is probably not up to date anymore here
            GenerateInstructions(instruction, context, body);
    }

    private void GenerateInstructions(Instruction instruction, MethodAnalysisContext context, CilMethodBody body)
    {
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
                instructions.Add(CilOpCodes.Ldstr, instruction.ToString());
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.Phi:
                instructions.Add(CilOpCodes.Ldstr, $"Phi opcodes should not exist at this point in decompilation ({instruction})");
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.Call:
            case OpCode.CallVoid:
            case OpCode.IndirectCall:
                instructions.Add(CilOpCodes.Ldstr, instruction.ToString());
                instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
                break;

            case OpCode.Return:
                if (!context.IsVoid && instruction.Operands.Count == 1)
                    LoadOperand(instruction.Operands[0], body);
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

    private void LoadOperand(object operand, CilMethodBody body)
    {
        var instructions = body.Instructions;

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
                instructions.Add(CilOpCodes.Ldloc, _locals[local]);
                break;
            case FieldReference field:
                instructions.Add(CilOpCodes.Ldarg_0); // TODO: Use local instead of 'this' without causing stack imbalance, i have no idea why that happens
                //instructions.Add(CilOpCodes.Ldloca, _locals[field.Local]);
                instructions.Add(CilOpCodes.Ldfld, field.Field.ToFieldDescriptor(_module!));
                break;
            default:
                instructions.Add(CilOpCodes.Ldstr, operand.ToString());
                break;
        }
    }
}
