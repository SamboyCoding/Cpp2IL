using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;

namespace Cpp2IL.Core;

public static class IlGenerator
{
    private const string HelpersNamespace = "Cpp2ILInjected";
    private const string HelpersTypeName = "Cpp2ILHelpers";
    private const string NoteIssueMethodName = "NoteDecompilerIssue";

    public static void InjectHelpersType(ApplicationAnalysisContext appContext)
    {
        var helpersType = appContext.InjectTypeIntoAllAssemblies(
            HelpersNamespace,
            HelpersTypeName,
            null,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);

        helpersType.InjectMethodToAllAssemblies(
            NoteIssueMethodName,
            appContext.SystemTypes.SystemVoidType,
            MethodAttributes.Public | MethodAttributes.Static,
            [appContext.SystemTypes.SystemStringType]);
    }

    public static void GenerateIl(MethodAnalysisContext context, MethodDefinition definition)
    {
        var assembly = context.DeclaringType!.DeclaringAssembly;
        var module = definition.DeclaringModule!;
        var factory = module.CorLibTypeFactory;

        var noteIssueContext = assembly
            .GetTypeByFullName($"{HelpersNamespace}.{HelpersTypeName}")?.Methods.FirstOrDefault(m => m.Name == NoteIssueMethodName);

        var writeLine = noteIssueContext != null
            ? noteIssueContext.ToMethodDescriptor()
            : factory.CorLibScope
                .CreateTypeReference("System", "Console")
                .CreateMemberReference("WriteLine", MethodSignature.CreateStatic(factory.Void, [factory.String]));

        // Change branch targets to instructions
        foreach (var instruction in context.ControlFlowGraph!.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Operands.Count > 0 && instruction.Operands[0] is Block target)
            {
                if (target.Instructions.Count > 0)
                    instruction.SetOperand(0, target.Instructions[0]);
            }
        }

        var body = new CilMethodBody()
        {
            InitializeLocals = true, // Without this ILSpy does: CompilerServices.Unsafe.SkipInit(out object obj);
            ComputeMaxStackOnBuild = false // There's stack imbalance somewhere, but this works for now
        };

        definition.CilMethodBody = body;

        // Make sure context.Locals actually has all locals across all blocks and operands
        var allLocals = new HashSet<LocalVariable>(context.Locals);
        foreach (var block in context.ControlFlowGraph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                foreach (var operand in instruction.Operands)
                {
                    CollectLocals(operand, allLocals);
                }
            }
        }
        context.Locals = allLocals.ToList();

        // Infer any remaining untyped locals from instructions that assign to them
        foreach (var local in context.Locals)
        {
            if (local.Type != null) continue;

            foreach (var inst in context.ControlFlowGraph!.Instructions)
            {
                if (inst.Operands.Count > 0 && ReferenceEquals(inst.Operands[0], local))
                {
                    if (inst.OpCode is >= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual)
                    {
                        local.Type = context.AppContext.SystemTypes.SystemBooleanType;
                        break;
                    }
                    if (inst.OpCode is OpCode.And or OpCode.Or or OpCode.Xor or OpCode.ShiftLeft or OpCode.ShiftRight or OpCode.Modulo)
                    {
                        local.Type = context.AppContext.SystemTypes.SystemInt32Type;
                        break;
                    }
                    if (inst.OpCode is OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide)
                    {
                        local.Type = context.AppContext.SystemTypes.SystemInt32Type;
                        break;
                    }
                    if (inst.OpCode == OpCode.Call && inst.Operands[0] is MethodAnalysisContext callee && !callee.IsVoid)
                    {
                        local.Type = callee.ReturnType;
                        break;
                    }
                    if (inst.OpCode == OpCode.Newobj && inst.Operands.Count > 1 && inst.Operands[1] is TypeAnalysisContext allocType)
                    {
                        local.Type = allocType;
                        break;
                    }
                    if (inst.OpCode == OpCode.NewArr && inst.Operands.Count > 1 && inst.Operands[1] is SzArrayTypeAnalysisContext arrType)
                    {
                        local.Type = arrType;
                        break;
                    }
                }
            }
        }

        // Map ISIL locals to IL
        Dictionary<LocalVariable, CilLocalVariable> locals = [];
        foreach (var local in context.Locals)
        {
            TypeSignature ilType;

            // Use object if type couldn't be determined, or if it's void, which no locals sig can hold
            if (local.Type != null && local.Type != context.AppContext.SystemTypes.SystemVoidType)
                ilType = local.Type.ToTypeSignature();
            else
                ilType = module.CorLibTypeFactory.Object;

            var ilLocal = new CilLocalVariable(ilType);
            body.LocalVariables.Add(ilLocal);
            locals.Add(local, ilLocal);
        }

        /* foreach (var instruction in context.ControlFlowGraph!.Instructions)
        {
            body.Instructions.Add(CilOpCodes.Ldstr, instruction.ToString());
            body.Instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!));
        }
        body.Instructions.Add(CilOpCodes.Ldstr, "-------------------------------------------------------------------------");
        body.Instructions.Add(CilOpCodes.Call, _importer!.ImportMethod(_writeLine!)); */

        // Generate IL
        Dictionary<Instruction, List<CilInstruction>> instructionMap = [];
        Dictionary<Block, CilInstruction> blockEntryMap = [];
        List<(CilInstruction BranchInstruction, Block TargetBlock)> pendingBlockBranchFixups = [];

        var entrySuccessor = context.ControlFlowGraph!.EntryBlock.Successors.FirstOrDefault(s => s != context.ControlFlowGraph.ExitBlock);
        var orderedBlocks = new List<Block>();
        if (entrySuccessor != null && context.ControlFlowGraph.Blocks.Contains(entrySuccessor))
            orderedBlocks.Add(entrySuccessor);

        foreach (var b in context.ControlFlowGraph.Blocks)
        {
            if (b != context.ControlFlowGraph.EntryBlock && b != context.ControlFlowGraph.ExitBlock && !orderedBlocks.Contains(b))
                orderedBlocks.Add(b);
        }

        foreach (var block in orderedBlocks)
        {
            if (block.Instructions.Count == 0)
                continue;

            foreach (var instruction in block.Instructions)
            {
                var generated = GenerateInstructions(instruction, context, definition, locals, writeLine);
                instructionMap.Add(instruction, generated);

                if (!blockEntryMap.ContainsKey(block) && generated.Count > 0)
                    blockEntryMap[block] = generated[0];
            }

            var lastInstruction = block.Instructions.Last();
            
            if (lastInstruction.OpCode == OpCode.ConditionalJump)
            {
                var trueTarget = TryResolveJumpTargetBlock(lastInstruction, context.ControlFlowGraph);
                var falseSuccessor = block.Successors.FirstOrDefault(s => s != trueTarget && s != context.ControlFlowGraph.ExitBlock);
                if (falseSuccessor == null) continue;
                var bridge = new CilInstruction(CilOpCodes.Br, new CilInstructionLabel());
                definition.CilMethodBody!.Instructions.Add(bridge);
                pendingBlockBranchFixups.Add((bridge, falseSuccessor));
            }

            else if (lastInstruction.OpCode != OpCode.Jump && lastInstruction.OpCode != OpCode.Return && lastInstruction.OpCode != OpCode.IndirectJump)
            {
                var successor = block.Successors.FirstOrDefault(s => s != context.ControlFlowGraph.ExitBlock);
                if (successor == null) continue;
                var bridge = new CilInstruction(CilOpCodes.Br, new CilInstructionLabel());
                definition.CilMethodBody!.Instructions.Add(bridge);
                pendingBlockBranchFixups.Add((bridge, successor));
            }
        }
        // Set IL branch targets
        foreach (var kvp in instructionMap)
        {
            var instruction = kvp.Key;
            var il = kvp.Value;

            if (instruction.OpCode == OpCode.Jump || instruction.OpCode == OpCode.ConditionalJump)
            {
                var ilBranch = il.First(i => i.OpCode == CilOpCodes.Br || i.OpCode == CilOpCodes.Brtrue);

                if (instruction.Operands[0] is Block targetBlock)
                {
                    context.AddWarning($"Branch target block not in cfg: {instruction} ({targetBlock})");
                    ilBranch.OpCode = CilOpCodes.Nop;
                    ilBranch.Operand = null;
                    continue;
                }

                var target = (Instruction)instruction.Operands[0];

                if (!instructionMap.ContainsKey(target))
                {
                    context.AddWarning($"Branch target not in ISIL to IL map: {instruction} --- {target}");
                    ilBranch.OpCode = CilOpCodes.Nop;
                    ilBranch.Operand = null;
                    continue;
                }

                ilBranch.Operand = new CilInstructionLabel(instructionMap[target][0]);
            }
        }
        
        foreach (var (branchInstruction, targetBlock) in pendingBlockBranchFixups)
        {
            var target = ResolveBlockEntryInstruction(targetBlock, blockEntryMap);
            if (target == null)
            {
                context.AddWarning($"Unable to resolve branch target block: {targetBlock}");
                branchInstruction.OpCode = CilOpCodes.Nop;
                branchInstruction.Operand = null;
                continue;
            }

            branchInstruction.Operand = new CilInstructionLabel(target);
        }

        var instructions = body.Instructions;
        
        // Peephole pass: eliminate redundant branch-to-next-instruction
        for (var i = 0; i < instructions.Count - 1; i++)
        {
            var cur = instructions[i];
            if (cur.OpCode.Code is CilCode.Br or CilCode.Br_S)
            {
                var next = instructions[i + 1];
                if (cur.Operand is CilInstructionLabel lbl && lbl.Instruction == next)
                {
                    cur.OpCode = CilOpCodes.Nop;
                    cur.Operand = null;
                }
            }
        }

        // Add analysis warnings
        foreach (var warning in context.AnalysisWarnings)
        {
            instructions.Add(CilOpCodes.Ldstr, Diagnostic("Warning: " + warning));
            instructions.Add(CilOpCodes.Call, writeLine);
        }

        // Ensure method body always ends with a valid terminator instruction
        if (instructions.Count == 0 || !IsTerminator(instructions[^1]))
        {
            if (!context.IsVoid)
                PushDefaultOf(context.ReturnType, definition, instructions);
            instructions.Add(CilOpCodes.Ret);
        }
    }

    private static bool IsTerminator(CilInstruction instruction)
    {
        var code = instruction.OpCode.Code;
        return code is CilCode.Ret or CilCode.Throw or CilCode.Rethrow
            or CilCode.Br or CilCode.Br_S or CilCode.Leave or CilCode.Leave_S;
    }

    // Limit so we don't run into the 16mb limit (see AsmResolver issue #775)
    private static string Diagnostic(string message) 
        => message.Length <= 250 ? message : message[..250] + "…";
    
    private static Block? TryResolveJumpTargetBlock(Instruction jumpInstruction, ISILControlFlowGraph cfg)
    {
        if (jumpInstruction.Operands.Count == 0)
            return null;

        if (jumpInstruction.Operands[0] is Block targetBlock)
            return targetBlock;

        if (jumpInstruction.Operands[0] is Instruction targetInstruction)
            return cfg.FindBlockByInstruction(targetInstruction);

        return null;
    }

    private static CilInstruction? ResolveBlockEntryInstruction(Block block,
        Dictionary<Block, CilInstruction> blockEntryMap, HashSet<Block>? visited = null)
    {
        if (blockEntryMap.TryGetValue(block, out var target))
            return target;

        visited ??= [];
        if (!visited.Add(block))
            return null;

        foreach (var successor in block.Successors)
        {
            var resolved = ResolveBlockEntryInstruction(successor, blockEntryMap, visited);
            if (resolved != null)
                return resolved;
        }
        return null;
    }

    private static List<CilInstruction> GenerateInstructions(Instruction instruction, MethodAnalysisContext context,
        MethodDefinition method, Dictionary<LocalVariable, CilLocalVariable> locals, IMethodDescriptor writeLine)
    {
        var body = method.CilMethodBody!;
        var instructions = body.Instructions;
        var currentCount = instructions.Count;
        var startIndex = instructions.Count;

        var module = method.DeclaringModule!;

        switch (instruction.OpCode)
        {
            case OpCode.Invalid:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Invalid instruction: {instruction}"));
                instructions.Add(CilOpCodes.Call, writeLine);
                break;

            case OpCode.NotImplemented:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Not implemented instruction: {instruction.Operands[0]}"));
                instructions.Add(CilOpCodes.Call, writeLine);
                break;

            case OpCode.Interrupt:
            case OpCode.Nop:
                instructions.Add(CilOpCodes.Nop);
                break;

            case OpCode.Move:
                if (instruction.Operands[0] is FieldReference field) // stfld takes instance before value so LoadOperand StoreToOperand doesn't work
                {
                    if (!field.Field.IsStatic)
                        LoadLocal(field.Local, method, locals);

                    LoadOperand(instruction.Operands[1], method, locals, writeLine, field.Field.FieldType);
                    instructions.Add(field.Field.IsStatic ? CilOpCodes.Stsfld : CilOpCodes.Stfld, field.Field.ToFieldDescriptor());
                    break;
                }

                // stelem needs array and index before the value, so like stfld it can't go through LoadOperand/StoreToOperand.
                // This also lets ILSpy handle it as a proper array initializer
                if (instruction.Operands[0] is ArrayAccess { Array.Type: SzArrayTypeAnalysisContext { ElementType: { } stored } } target)
                {
                    LoadLocal(target.Array, method, locals);
                    LoadOperand(target.Index, method, locals, writeLine);
                    LoadOperand(instruction.Operands[1], method, locals, writeLine, stored);
                    instructions.Add(CilOpCodes.Stelem, stored.ToTypeSignature().ToTypeDefOrRef());
                    break;
                }

                if (instruction.Operands[0] is MemoryOperand memTarget && memTarget.Base is LocalVariable { Type: SzArrayTypeAnalysisContext memArrType } memArrLocal && memTarget.Addend >= 0x20)
                {
                    var elemType = memArrType.ElementType;
                    var elemSize = elemType.IsValueType ? Math.Max(1, (int)TypeSizes.UnboxedSize(elemType, context.AppContext.Binary.PointerSizeBytes)) : context.AppContext.Binary.PointerSizeBytes;
                    var elemIdx = (int)((memTarget.Addend - 0x20) / elemSize);
                    LoadLocal(memArrLocal, method, locals);
                    instructions.Add(CilOpCodes.Ldc_I4, elemIdx);
                    LoadOperand(instruction.Operands[1], method, locals, writeLine, elemType);
                    instructions.Add(CilOpCodes.Stelem, elemType.ToTypeSignature().ToTypeDefOrRef());
                    break;
                }

                var destType = DestinationType(instruction.Operands[0], context.AppContext);
                LoadOperand(instruction.Operands[1], method, locals, writeLine, destType);
                var srcType = (instruction.Operands[1] as LocalVariable)?.Type ?? (instruction.Operands[1] as FieldReference)?.Field.FieldType;
                StoreToOperand(instruction.Operands[0], method, locals, writeLine, srcType);
                break;

            case OpCode.NewArr:
                if (instruction.Operands is [_, SzArrayTypeAnalysisContext { ElementType: { } newArrayElement }, { } length])
                {
                    LoadOperand(length, method, locals, writeLine);
                    instructions.Add(CilOpCodes.Newarr, newArrayElement.ToTypeSignature().ToTypeDefOrRef());
                }
                else
                    instructions.Add(CilOpCodes.Ldnull);

                StoreToOperand(instruction.Operands[0], method, locals, writeLine);
                break;

            case OpCode.Newobj:
                // Try and fuse our Newobj + the follow up constructor CallVoid into one IL newobj.
                // If we can't, just fall back to an Ldnull.
                if (FindConstructorCall(context, instruction) is { Operands: [MethodAnalysisContext constructor, _, ..] } constructorCall)
                {
                    // Operands run [ctor, newObject, arguments..., methodInfo], so take only as many as
                    // the constructor declares (i.e. drop methodInfo)
                    var constructorArgs = constructorCall.Operands.Skip(ConstructorReceiverIndex(constructorCall) + 1).Take(constructor.Parameters.Count).ToList();
                    for (var i = 0; i < constructorArgs.Count; i++)
                        LoadOperand(constructorArgs[i], method, locals, writeLine, constructor.Parameters[i].ParameterType);

                    instructions.Add(CilOpCodes.Newobj, constructor.ToMethodDescriptor());
                    StoreToOperand(instruction.Operands[0], method, locals, writeLine);

                    constructorCall.OpCode = OpCode.Nop;
                    constructorCall.SetOperands();
                }
                else if (instruction.Operands is [_, TypeAnalysisContext allocatedType] && allocatedType.Methods.FirstOrDefault(m => m is { Name: ".ctor", Parameters.Count: 0 }) is { } parameterlessCtor)
                {
                    // Nothing to fuse with, so the allocation was self-contained. The type is still right, so construct it bare.
                    instructions.Add(CilOpCodes.Newobj, parameterlessCtor.ToMethodDescriptor());
                    StoreToOperand(instruction.Operands[0], method, locals, writeLine);
                }
                else
                {
                    instructions.Add(CilOpCodes.Ldnull);
                    StoreToOperand(instruction.Operands[0], method, locals, writeLine);
                }
                break;

            case OpCode.Box:
                if (instruction.Operands is [_, TypeAnalysisContext boxedType, var boxedValue])
                {
                    // il2cpp_value_box takes the value by address, but IL boxes it by value
                    LoadOperand(boxedValue is AddressOf { Target: LocalVariable byRef } ? byRef : boxedValue, method, locals, writeLine, boxedType);
                    instructions.Add(CilOpCodes.Box, boxedType.ToTypeSignature().ToTypeDefOrRef());
                }
                else
                    instructions.Add(CilOpCodes.Ldnull);

                StoreToOperand(instruction.Operands[0], method, locals, writeLine);
                break;

            case OpCode.Throw:
                if (instruction.Operands is [TypeAnalysisContext exceptionType]
                    && exceptionType.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0) is { } exceptionCtor)
                    instructions.Add(CilOpCodes.Newobj, exceptionCtor.ToMethodDescriptor());
                else if (instruction.Operands is [LocalVariable or FieldReference])
                    LoadOperand(instruction.Operands[0], method, locals, writeLine); // an already-constructed exception
                else
                    instructions.Add(CilOpCodes.Ldnull);

                instructions.Add(CilOpCodes.Throw);
                break;

            case OpCode.Phi:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Phi opcodes should not exist at this point in decompilation ({instruction})"));
                instructions.Add(CilOpCodes.Call, writeLine);
                if (instruction.Operands.Count > 0 && instruction.Operands[0] is LocalVariable phiDest)
                {
                    if (instruction.Operands.Count > 1)
                        LoadOperand(instruction.Operands[1], method, locals, writeLine);
                    else
                        PushDefaultOf(phiDest.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                    StoreToOperand(phiDest, method, locals, writeLine);
                }
                break;

            case OpCode.Call:
            case OpCode.CallVoid:
                MethodAnalysisContext? targetMethod = instruction.Operands[0] as MethodAnalysisContext;
                if (targetMethod == null)
                {
                    if (instruction.Operands[0] is Immediate targetAddress)
                    {
                        var resolvedTarget = MetadataResolver.FollowBranchTrampolines(context.AppContext, targetAddress.UnsignedValue);
                        if (context.AppContext.MethodsByAddress.TryGetValue(resolvedTarget, out var methods) && methods.Count > 0)
                        {
                            targetMethod = methods[0];
                        }
                        else if (context.AppContext.Binary.ConcreteGenericImplementationsByAddress.TryGetValue(resolvedTarget, out var genericRefs) && genericRefs.Count > 0)
                        {
                            targetMethod = context.AppContext.ResolveContextForMethod(genericRefs[0].BaseMethod);
                        }
                    }
                }

                if (targetMethod == null)
                {
                    if (instruction.Operands[0] is Immediate targetAddress)
                    {
                        instructions.Add(CilOpCodes.Ldstr, $"Method not found @{targetAddress.UnsignedValue:X}");
                        instructions.Add(CilOpCodes.Call, writeLine);
                        if (instruction.OpCode == OpCode.Call && instruction.Operands.Count > 1 && instruction.Operands[1] is LocalVariable retLocal)
                        {
                            PushDefaultOf(retLocal.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                            StoreToOperand(retLocal, method, locals, writeLine);
                        }
                        break;
                    }
                    else if (instruction.Operands[0] is StringLiteral { Value: var kfName })
                    {
                        if (kfName is "SzArrayNew" or "il2cpp_vm_array_new_specific" or "il2cpp_array_new_specific")
                        {
                            if (instruction.Operands.Count > 1 && instruction.Operands[1] is LocalVariable arrResult)
                            {
                                if (arrResult.Type is SzArrayTypeAnalysisContext szArr)
                                {
                                    if (instruction.Operands.Count > 3)
                                        LoadOperand(instruction.Operands[3], method, locals, writeLine);
                                    else
                                        instructions.Add(CilOpCodes.Ldc_I4_0);
                                    instructions.Add(CilOpCodes.Newarr, szArr.ElementType.ToTypeSignature().ToTypeDefOrRef());
                                }
                                else
                                {
                                    PushDefaultOf(arrResult.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                                }
                                StoreToOperand(arrResult, method, locals, writeLine);
                            }
                            break;
                        }

                        if (kfName == "il2cpp_vm_object_box")
                        {
                            if (instruction.Operands.Count > 1 && instruction.Operands[1] is LocalVariable boxResult)
                            {
                                if (instruction.Operands.Count > 3 && instruction.Operands[2] is TypeAnalysisContext targetBoxedType)
                                {
                                    LoadOperand(instruction.Operands[3], method, locals, writeLine, targetBoxedType);
                                    instructions.Add(CilOpCodes.Box, targetBoxedType.ToTypeSignature().ToTypeDefOrRef());
                                }
                                else
                                {
                                    PushDefaultOf(boxResult.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                                }
                                StoreToOperand(boxResult, method, locals, writeLine);
                            }
                            break;
                        }

                        // For other key functions or class_init, cleanly initialize return value if any
                        if (instruction.OpCode == OpCode.Call && instruction.Operands.Count > 1 && instruction.Operands[1] is LocalVariable retLocal)
                        {
                            if (context.ControlFlowGraph!.Instructions.Any(i => i != instruction && i.Operands.Contains(retLocal)))
                            {
                                PushDefaultOf(retLocal.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                                StoreToOperand(retLocal, method, locals, writeLine);
                            }
                        }
                        break;
                    }
                    else
                    {
                        instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Unknown call target operand: {instruction.Operands[0]}"));
                        instructions.Add(CilOpCodes.Call, writeLine);
                        // Any remaining unknown target operand: safely provide default return value
                        if (instruction.OpCode == OpCode.Call && instruction.Operands.Count > 1 && instruction.Operands[1] is LocalVariable retLocal)
                        {
                            if (context.ControlFlowGraph!.Instructions.Any(i => i != instruction && i.Operands.Contains(retLocal)))
                            {
                                PushDefaultOf(retLocal.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                                StoreToOperand(retLocal, method, locals, writeLine);
                            }
                        }
                        break;
                    }
                }

                var importedMethod = targetMethod.ToMethodDescriptor();

                var thisParamIndex = instruction.OpCode == OpCode.Call ? 2 : 1;

                if (!targetMethod.IsStatic) // Load 'this' param
                {
                    if ((instruction.Operands.Count - 1) >= thisParamIndex)
                        LoadOperand(instruction.Operands[thisParamIndex], method, locals, writeLine, targetMethod.DeclaringType);
                    else
                    {
                        instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Non static method called without 'this' param ({instruction})"));
                        instructions.Add(CilOpCodes.Call, writeLine);
                        instructions.Add(CilOpCodes.Ldnull);
                    }
                }

                // Load normal params
                var callParamIndex = instruction.OpCode == OpCode.Call ? (targetMethod.IsStatic ? 2 : 3) : (targetMethod.IsStatic ? 1 : 2);
                // A call whose target was only identified after lifting still carries the operands the
                // unknown-callee convention gave it, which may be fewer than the method actually takes.
                // The stack still has to match the signature, so anything missing gets a placeholder.
                var availableArgs = instruction.Operands.Count - callParamIndex;
                for (var i = 0; i < targetMethod.Parameters.Count; i++)
                {
                    var parameterType = targetMethod.Parameters[i].ParameterType;

                    if (i < availableArgs)
                        LoadOperand(instruction.Operands[callParamIndex + i], method, locals, writeLine, parameterType);
                    else
                        PushDefaultOf(parameterType, method, instructions);
                }

                instructions.Add(CilOpCodes.Call, importedMethod);

                // the lifter's guess at whether the callee returns anything can disagree with the
                // signature we later resolved, so go by the signature and balance the stack
                if (!targetMethod.IsVoid)
                {
                    if (instruction.OpCode == OpCode.Call)
                        StoreToOperand(instruction.Operands[1], method, locals, writeLine, targetMethod.ReturnType);
                    else
                        instructions.Add(CilOpCodes.Pop);
                }

                break;

            case OpCode.IndirectCall:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Indirect call: {instruction.Operands[0]} (should have been resolved before IL gen)"));
                instructions.Add(CilOpCodes.Call, writeLine);
                if (instruction.Operands.Count > 1 && instruction.Operands[1] is LocalVariable indirectRet)
                {
                    PushDefaultOf(indirectRet.Type ?? context.AppContext.SystemTypes.SystemObjectType, method, instructions);
                    StoreToOperand(indirectRet, method, locals, writeLine);
                }
                break;

            case OpCode.Return:
                if (!context.IsVoid)
                {
                    if (instruction.Operands.Count == 1)
                        LoadOperand(instruction.Operands[0], method, locals, writeLine, context.ReturnType);
                    else
                        PushDefaultOf(context.ReturnType, method, instructions);
                }
                instructions.Add(CilOpCodes.Ret);
                break;

            case OpCode.Jump:
                instructions.Add(CilOpCodes.Br, new CilInstructionLabel());
                break;

            case OpCode.ConditionalJump:
                LoadOperand(instruction.Operands[1], method, locals, writeLine);
                instructions.Add(CilOpCodes.Brtrue, new CilInstructionLabel());
                break;

            case OpCode.IndirectJump:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Indirect jump: {instruction.Operands[0]} (should have been resolved before IL gen)"));
                instructions.Add(CilOpCodes.Call, writeLine);
                if (!context.IsVoid)
                    PushDefaultOf(context.ReturnType, method, instructions);
                instructions.Add(CilOpCodes.Ret);
                break;

            case OpCode.ShiftStack:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Stack shift: {instruction} (stack analysis should have removed these)"));
                instructions.Add(CilOpCodes.Call, writeLine);
                break;

            case OpCode.CheckEqual:
            case OpCode.CheckGreater:
            case OpCode.CheckLess:
            case OpCode.CheckNotEqual:
            case OpCode.CheckGreaterOrEqual:
            case OpCode.CheckLessOrEqual:

            case OpCode.Add:
            case OpCode.Subtract:
            case OpCode.Multiply:
            case OpCode.Divide:
            case OpCode.Modulo:

            case OpCode.ShiftLeft:
            case OpCode.ShiftRight:

            case OpCode.And:
            case OpCode.Or:
            case OpCode.Xor:
                // klass pointer read => GetType
                if (instruction.OpCode is OpCode.CheckEqual or OpCode.CheckNotEqual
                    && TryEmitExactTypeComparison(instruction, method, locals, writeLine))
                    break;

                // Float arithmetic on a promoted integer operand needs an explicit conversion, so both
                // operands are coerced to the (float) result type. A no-op when they already match.
                var floatConversion = FloatArithmeticConversion(instruction);
                var isNumericOp = instruction.OpCode is OpCode.Add or OpCode.Subtract or OpCode.Multiply
                    or OpCode.Divide or OpCode.Modulo or OpCode.ShiftLeft or OpCode.ShiftRight
                    or OpCode.And or OpCode.Or or OpCode.Xor;

                var isComparison = instruction.OpCode is >= OpCode.CheckEqual and <= OpCode.CheckLessOrEqual;
                var operandType = DestinationType(instruction.Operands[0], context.AppContext);
                if (isNumericOp && (operandType == null || !operandType.IsValueType))
                    operandType = context.AppContext.SystemTypes.SystemInt32Type;

                if (isComparison)
                {
                    operandType = (instruction.Operands[1] as LocalVariable)?.Type
                        ?? (instruction.Operands[1] as FieldReference)?.Field.FieldType
                        ?? (instruction.Operands[2] as LocalVariable)?.Type
                        ?? (instruction.Operands[2] as FieldReference)?.Field.FieldType;
                }

                LoadOperand(instruction.Operands[1], method, locals, writeLine, operandType);
                if (floatConversion is { } conv1)
                    instructions.Add(conv1);
                LoadOperand(instruction.Operands[2], method, locals, writeLine, operandType);
                if (floatConversion is { } conv2)
                    instructions.Add(conv2);

                switch (instruction.OpCode)
                {
                    case OpCode.CheckEqual: instructions.Add(CilOpCodes.Ceq); break;
                    case OpCode.CheckGreater: instructions.Add(CilOpCodes.Cgt); break;
                    case OpCode.CheckLess: instructions.Add(CilOpCodes.Clt); break;

                    // a != b  ==  (a == b) == 0
                    case OpCode.CheckNotEqual:
                        instructions.Add(CilOpCodes.Ceq);
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Ceq);
                        break;
                    // a >= b  ==  !(a < b)
                    case OpCode.CheckGreaterOrEqual:
                        instructions.Add(CilOpCodes.Clt);
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Ceq);
                        break;
                    // a <= b  ==  !(a > b)
                    case OpCode.CheckLessOrEqual:
                        instructions.Add(CilOpCodes.Cgt);
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Ceq);
                        break;

                    case OpCode.Add: instructions.Add(CilOpCodes.Add); break;
                    case OpCode.Subtract: instructions.Add(CilOpCodes.Sub); break;
                    case OpCode.Multiply: instructions.Add(CilOpCodes.Mul); break;
                    case OpCode.Divide: instructions.Add(CilOpCodes.Div); break;
                    case OpCode.Modulo: instructions.Add(CilOpCodes.Rem); break;

                    case OpCode.ShiftLeft: instructions.Add(CilOpCodes.Shl); break;
                    case OpCode.ShiftRight: instructions.Add(CilOpCodes.Shr); break;

                    case OpCode.And: instructions.Add(CilOpCodes.And); break;
                    case OpCode.Or: instructions.Add(CilOpCodes.Or); break;
                    case OpCode.Xor: instructions.Add(CilOpCodes.Xor); break;
                }

                var resType = isComparison ? context.AppContext.SystemTypes.SystemBooleanType : operandType;
                StoreToOperand(instruction.Operands[0], method, locals, writeLine, resType);
                break;

            case OpCode.Not:
            case OpCode.Negate:
                var isBool = IsBoolean(instruction.Operands[1], context)
                    || (instruction.Operands[0] is LocalVariable destLoc && destLoc.Type == context.AppContext.SystemTypes.SystemBooleanType);

                LoadOperand(instruction.Operands[1], method, locals, writeLine, isBool ? context.AppContext.SystemTypes.SystemBooleanType : null);

                if (instruction.OpCode == OpCode.Negate)
                    instructions.Add(CilOpCodes.Neg);
                else if (isBool)
                {
                    instructions.Add(CilOpCodes.Ldc_I4_0);
                    instructions.Add(CilOpCodes.Ceq);
                }
                else
                    instructions.Add(CilOpCodes.Not);

                var notType = isBool ? context.AppContext.SystemTypes.SystemBooleanType : DestinationType(instruction.Operands[0], context.AppContext);
                StoreToOperand(instruction.Operands[0], method, locals, writeLine, notType);
                break;

            default:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Unknown instruction: {instruction}"));
                instructions.Add(CilOpCodes.Call, writeLine);
                break;
        }

        return instructions.ToList().GetRange(startIndex, instructions.Count - startIndex); // Return added IL
    }
    
    private static int ConstructorReceiverIndex(Instruction constructorCall) => constructorCall.OpCode == OpCode.CallVoid ? 1 : 2;

    // Try find the follow up CallVoid for a constructor, after a Newobj.
    private static Instruction? FindConstructorCall(MethodAnalysisContext context, Instruction newobj)
    {
        var newObject = newobj.Operands[0];

        // The allocation and the constructor call routinely end up in different blocks
        var instructions = context.ControlFlowGraph!.Instructions;
        var index = instructions.IndexOf(newobj);

        if (index < 0)
            return null;

        for (var i = index + 1; i < instructions.Count; i++)
        {
            var candidate = instructions[i];

            if (candidate is not { OpCode: OpCode.Call or OpCode.CallVoid, Operands: [MethodAnalysisContext { Name: ".ctor" }, ..] })
                continue;

            var receiver = ConstructorReceiverIndex(candidate);

            if (candidate.Operands.Count > receiver && ReferenceEquals(candidate.Operands[receiver], newObject))
                return candidate;
        }

        return null;
    }

    private static CilOpCode? FloatArithmeticConversion(Instruction instruction)
    {
        if (instruction.OpCode is not (OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide or OpCode.Modulo))
            return null;

        return (instruction.Operands[0] as LocalVariable)?.Type?.FullName switch
        {
            "System.Single" => CilOpCodes.Conv_R4,
            "System.Double" => CilOpCodes.Conv_R8,
            _ => null,
        };
    }

    private static void LoadOperand(IOperand operand, MethodDefinition method,
        Dictionary<LocalVariable, CilLocalVariable> locals, IMethodDescriptor writeLine,
        TypeAnalysisContext? expectedType = null)
    {
        var instructions = method.CilMethodBody!.Instructions;

        var module = method.DeclaringModule!;

        // A null reference reaches us as an integer zero, which would otherwise be emitted as a literal 0
        // and read back as a cast from a number.
        if (expectedType is { IsValueType: false } && IsZeroConstant(operand))
        {
            instructions.Add(CilOpCodes.Ldnull);
            return;
        }

        switch (operand)
        {
            case Immediate { Value: >= int.MinValue and <= int.MaxValue } immediate:
                if (expectedType?.FullName == "System.Single")
                    instructions.Add(CilOpCodes.Ldc_R4, (float)immediate.Value);
                else if (expectedType?.FullName == "System.Double")
                    instructions.Add(CilOpCodes.Ldc_R8, (double)immediate.Value);
                else if (expectedType?.FullName is "System.Int64" or "System.UInt64")
                    instructions.Add(CilOpCodes.Ldc_I8, immediate.Value);
                else
                {
                    instructions.Add(CilOpCodes.Ldc_I4, (int)immediate.Value);
                    if (expectedType?.FullName is "System.IntPtr" or "System.UIntPtr")
                        instructions.Add(CilOpCodes.Conv_I);
                }
                break;
            case Immediate immediate:
                if (expectedType?.FullName == "System.Double")
                    instructions.Add(CilOpCodes.Ldc_R8, (double)immediate.Value);
                else if (expectedType?.FullName is "System.Int32" or "System.UInt32" or "System.Boolean")
                {
                    instructions.Add(CilOpCodes.Ldc_I8, immediate.Value);
                    instructions.Add(CilOpCodes.Conv_I4);
                }
                else
                {
                    instructions.Add(CilOpCodes.Ldc_I8, immediate.Value);
                    if (expectedType?.FullName is "System.IntPtr" or "System.UIntPtr")
                        instructions.Add(CilOpCodes.Conv_I);
                }
                break;
            case FloatLiteral f:
                instructions.Add(CilOpCodes.Ldc_R4, f.Value);
                break;
            case DoubleLiteral d:
                instructions.Add(CilOpCodes.Ldc_R8, d.Value);
                break;
            case StringLiteral s:
                if (expectedType != null && expectedType.IsValueType)
                    PushDefaultOf(expectedType, method, instructions);
                else
                    instructions.Add(CilOpCodes.Ldstr, s.Value);
                break;
            case LocalVariable local:
                LoadLocal(local, method, locals);
                if (expectedType != null)
                {
                    if (expectedType.FullName == "System.Object" && local.Type is { IsValueType: true } valType)
                    {
                        instructions.Add(CilOpCodes.Box, valType.ToTypeSignature().ToTypeDefOrRef());
                    }
                    else if (expectedType.IsValueType && local.Type is { IsValueType: false } and not null)
                    {
                        instructions.Add(CilOpCodes.Unbox_Any, expectedType.ToTypeSignature().ToTypeDefOrRef());
                    }
                    else if (expectedType.FullName is "System.IntPtr" or "System.UIntPtr"
                             && local.Type?.FullName is "System.Int32" or "System.UInt32")
                    {
                        instructions.Add(CilOpCodes.Conv_I);
                    }
                    else if (expectedType.FullName is "System.Int32" or "System.UInt32"
                             && local.Type?.FullName is "System.IntPtr" or "System.UIntPtr" or "System.Int64" or "System.UInt64")
                    {
                        instructions.Add(CilOpCodes.Conv_I4);
                    }
                }
                break;
            case ArrayLength arrayLength:
                LoadLocal(arrayLength.Array, method, locals);
                instructions.Add(CilOpCodes.Ldlen);
                instructions.Add(CilOpCodes.Conv_I4);
                break;
            case AddressOf { Target: LocalVariable addressed }:
                instructions.Add(CilOpCodes.Ldloca, GetOrCreateLocal(addressed, method, locals));
                break;
            case AddressOf { Target: ArrayAccess elementAddress }:
                LoadLocal(elementAddress.Array, method, locals);
                LoadOperand(elementAddress.Index, method, locals, writeLine);
                instructions.Add(CilOpCodes.Ldelema,
                    ((SzArrayTypeAnalysisContext)elementAddress.Array.Type!).ElementType.ToTypeSignature().ToTypeDefOrRef());
                break;
            case ArrayAccess arrayAccess:
                LoadLocal(arrayAccess.Array, method, locals);
                LoadOperand(arrayAccess.Index, method, locals, writeLine);
                instructions.Add(CilOpCodes.Ldelem,
                    ((SzArrayTypeAnalysisContext)arrayAccess.Array.Type!).ElementType.ToTypeSignature().ToTypeDefOrRef());
                break;
            case FieldReference field:
                if (field.Field.IsStatic)
                {
                    instructions.Add(CilOpCodes.Ldsfld, field.Field.ToFieldDescriptor());
                    break;
                }

                LoadLocal(field.Local, method, locals);
                instructions.Add(CilOpCodes.Ldfld, field.Field.ToFieldDescriptor());
                break;
            case MemoryOperand memory:
                if (memory.Index == null && memory.Addend == 0 && memory.Scale == 0
                    && memory.Base is LocalVariable local2)
                {
                    LoadLocal(local2, method, locals);

                    // A load through a managed pointer (byref) dereferences it to yield the referent.
                    if (local2.Type is ByRefTypeAnalysisContext { ElementType: { } referent })
                        instructions.Add(referent.IsValueType
                            ? new CilInstruction(CilOpCodes.Ldobj, referent.ToTypeSignature().ToTypeDefOrRef())
                            : new CilInstruction(CilOpCodes.Ldind_Ref));
                    break;
                }

                // String properties: Length and char indexer
                if (memory.Base is LocalVariable { Type.FullName: "System.String" } strLocal)
                {
                    if (memory.Index == null && memory.Addend == 0x10)
                    {
                        var getLength = module.CorLibTypeFactory.CorLibScope
                            .CreateTypeReference("System", "String")
                            .CreateMemberReference("get_Length", MethodSignature.CreateInstance(module.CorLibTypeFactory.Int32));
                        LoadLocal(strLocal, method, locals);
                        instructions.Add(CilOpCodes.Callvirt, getLength);
                        break;
                    }
                    if (memory.Addend == 0x14 && memory.Index != null)
                    {
                        var getChars = module.CorLibTypeFactory.CorLibScope
                            .CreateTypeReference("System", "String")
                            .CreateMemberReference("get_Chars", MethodSignature.CreateInstance(module.CorLibTypeFactory.Char, [module.CorLibTypeFactory.Int32]));
                        LoadLocal(strLocal, method, locals);
                        LoadOperand(memory.Index, method, locals, writeLine);
                        instructions.Add(CilOpCodes.Callvirt, getChars);
                        break;
                    }
                }

                // Array: Length and element access
                if (memory.Base is LocalVariable { Type: SzArrayTypeAnalysisContext arrType } arrLocal)
                {
                    if (memory.Index == null && memory.Addend == 0x18)
                    {
                        LoadLocal(arrLocal, method, locals);
                        instructions.Add(CilOpCodes.Ldlen);
                        instructions.Add(CilOpCodes.Conv_I4);
                        break;
                    }
                    if (memory.Index != null)
                    {
                        LoadLocal(arrLocal, method, locals);
                        LoadOperand(memory.Index, method, locals, writeLine);
                        instructions.Add(CilOpCodes.Ldelem, arrType.ElementType.ToTypeSignature().ToTypeDefOrRef());
                        break;
                    }
                }

                // Cleanly handle internal runtime metadata structures (RGCTX tables, Il2CppClass tables, static field storage)
                if (memory.Base is LocalVariable baseLocal && (baseLocal.Type is RgctxTableTypeAnalysisContext or MethodRgctxTableTypeAnalysisContext
                    or RuntimeClassTypeAnalysisContext or StaticFieldStorageTypeAnalysisContext
                    || baseLocal.Type?.FullName?.Contains("Il2CppRgctx") == true || baseLocal.Type?.FullName?.Contains("Il2CppClass") == true
                    || memory.Addend is 0xB8 or 0x130 or 0x135 or 0x138))
                {
                    if (expectedType != null)
                        PushDefaultOf(expectedType, method, instructions);
                    else
                    {
                        instructions.Add(CilOpCodes.Ldc_I4_0);
                        instructions.Add(CilOpCodes.Conv_I);
                    }
                    break;
                }

                // Cleanly handle absolute uninitialized metadata slots or data globals (memory.Base == null)
                if (memory.Base == null)
                {
                    if (expectedType != null)
                        PushDefaultOf(expectedType, method, instructions);
                    else
                        instructions.Add(CilOpCodes.Ldnull);
                    break;
                }

                instructions.Add(CilOpCodes.Ldstr, Diagnostic("Unmanaged memory load: " + operand));
                instructions.Add(CilOpCodes.Call, writeLine);
                if (expectedType != null)
                {
                    PushDefaultOf(expectedType, method, instructions);
                }
                else if (memory.Base is LocalVariable lv && lv.Type != null && !lv.Type.IsValueType)
                {
                    instructions.Add(CilOpCodes.Ldnull);
                }
                else
                {
                    instructions.Add(CilOpCodes.Ldc_I4_0);
                    instructions.Add(CilOpCodes.Conv_I);
                }
                break;
            case RuntimeMethodInfoAnalysisContext runtimeMethod:
                // A delegate constructor takes its target as a native pointer, which is exactly ldftn.
                if (expectedType?.FullName == "System.IntPtr")
                {
                    instructions.Add(CilOpCodes.Ldftn, runtimeMethod.RepresentedMethod.ToMethodDescriptor());
                    break;
                }

                //Not fully implemented, these basically shouldn't actually ever exist in the final IL.
                if (expectedType != null)
                    PushDefaultOf(expectedType, method, instructions);
                else
                {
                    instructions.Add(CilOpCodes.Ldc_I4_0);
                    instructions.Add(CilOpCodes.Conv_I);
                }
                break;
            case RuntimeFieldInfoAnalysisContext runtimeField:
                // fieldof(F), e.g. the handle InitializeArray takes.
                if (expectedType?.FullName == "System.RuntimeFieldHandle")
                {
                    instructions.Add(CilOpCodes.Ldtoken, runtimeField.RepresentedField.ToFieldDescriptor());
                    break;
                }

                if (expectedType != null)
                    PushDefaultOf(expectedType, method, instructions);
                else
                {
                    instructions.Add(CilOpCodes.Ldc_I4_0);
                    instructions.Add(CilOpCodes.Conv_I);
                }
                break;
            case RuntimeClassTypeAnalysisContext or RgctxTableTypeAnalysisContext
                or MethodRgctxTableTypeAnalysisContext or StaticFieldStorageTypeAnalysisContext:
                if (expectedType != null)
                    PushDefaultOf(expectedType, method, instructions);
                else
                {
                    instructions.Add(CilOpCodes.Ldc_I4_0);
                    instructions.Add(CilOpCodes.Conv_I);
                }
                break;
            case TypeAnalysisContext type:
                //typeof(T)
                var corLibScope = module.CorLibTypeFactory.CorLibScope;
                var typeFromHandle = corLibScope
                    .CreateTypeReference("System", "Type")
                    .CreateMemberReference("GetTypeFromHandle", MethodSignature.CreateStatic(
                        corLibScope.CreateTypeReference("System", "Type").ToTypeSignature(false),
                        [corLibScope.CreateTypeReference("System", "RuntimeTypeHandle").ToTypeSignature(true)]));

                instructions.Add(CilOpCodes.Ldtoken, type.ToTypeSignature().ToTypeDefOrRef());
                instructions.Add(CilOpCodes.Call, typeFromHandle);
                break;
            default:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic("Unknown operand: " + operand));
                instructions.Add(CilOpCodes.Call, writeLine);
                if (expectedType != null)
                    PushDefaultOf(expectedType, method, instructions);
                else
                    instructions.Add(CilOpCodes.Ldnull);
                break;
        }
    }
    
    private static bool TryEmitExactTypeComparison(Instruction instruction, MethodDefinition method,
        Dictionary<LocalVariable, CilLocalVariable> locals, IMethodDescriptor writeLine)
    {
        var left = instruction.Operands[1];
        var right = instruction.Operands[2];

        IOperand typeOperand;
        LocalVariable objLocal;

        if (left is TypeAnalysisContext && IsKlassPointerLoad(right, out var rightLocal))
            (typeOperand, objLocal) = (left, rightLocal);
        else if (right is TypeAnalysisContext && IsKlassPointerLoad(left, out var leftLocal))
            (typeOperand, objLocal) = (right, leftLocal);
        else
            return false;

        var module = method.DeclaringModule!;
        var instructions = method.CilMethodBody!.Instructions;

        var getType = module.CorLibTypeFactory.CorLibScope
            .CreateTypeReference("System", "Object")
            .CreateMemberReference("GetType", MethodSignature.CreateInstance(
                module.CorLibTypeFactory.CorLibScope.CreateTypeReference("System", "Type").ToTypeSignature(false)));

        LoadLocal(objLocal, method, locals);
        instructions.Add(CilOpCodes.Callvirt, getType);
        LoadOperand(typeOperand, method, locals, writeLine); // emits typeof(T)
        instructions.Add(CilOpCodes.Ceq);

        if (instruction.OpCode == OpCode.CheckNotEqual)
        {
            instructions.Add(CilOpCodes.Ldc_I4_0);
            instructions.Add(CilOpCodes.Ceq);
        }

        StoreToOperand(instruction.Operands[0], method, locals, writeLine);
        return true;
    }
    
    private static bool IsKlassPointerLoad(IOperand operand, out LocalVariable local)
    {
        if (operand is MemoryOperand { Index: null, Addend: 0, Scale: 0, Base: LocalVariable { Type.IsValueType: false } baseLocal })
        {
            local = baseLocal;
            return true;
        }

        local = null!;
        return false;
    }

    private static void PushDefaultOf(TypeAnalysisContext? type, MethodDefinition method, CilInstructionCollection instructions)
    {
        if (type == null)
        {
            instructions.Add(CilOpCodes.Ldnull);
            return;
        }

        if (type.FullName == "System.Single")
        {
            instructions.Add(CilOpCodes.Ldc_R4, 0f);
            return;
        }
        if (type.FullName == "System.Double")
        {
            instructions.Add(CilOpCodes.Ldc_R8, 0d);
            return;
        }
        if (type.FullName is "System.Int64" or "System.UInt64")
        {
            instructions.Add(CilOpCodes.Ldc_I8, 0L);
            return;
        }
        if (type.FullName is "System.IntPtr" or "System.UIntPtr")
        {
            instructions.Add(CilOpCodes.Ldc_I4_0);
            instructions.Add(CilOpCodes.Conv_I);
            return;
        }
        if (type.FullName is "System.Boolean" or "System.Byte" or "System.SByte"
            or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Char")
        {
            instructions.Add(CilOpCodes.Ldc_I4_0);
            return;
        }

        if (type.IsValueType || type is GenericParameterTypeAnalysisContext)
        {
            var typeSig = type.ToTypeSignature();
            var scratch = new CilLocalVariable(typeSig);
            method.CilMethodBody!.LocalVariables.Add(scratch);

            instructions.Add(CilOpCodes.Ldloca, scratch);
            instructions.Add(CilOpCodes.Initobj, typeSig.ToTypeDefOrRef());
            instructions.Add(CilOpCodes.Ldloc, scratch);
            return;
        }

        instructions.Add(CilOpCodes.Ldnull);
    }

    private static bool IsBoolean(IOperand operand, MethodAnalysisContext context) =>
        (operand is LocalVariable { Type: { } type } && type == context.AppContext.SystemTypes.SystemBooleanType)
        || (operand is FieldReference field && field.Field.FieldType == context.AppContext.SystemTypes.SystemBooleanType);

    private static bool IsZeroConstant(IOperand operand) => operand is Immediate { Value: 0 };
    
    private static TypeAnalysisContext? DestinationType(IOperand destination, ApplicationAnalysisContext? appContext = null) =>
        destination switch
        {
            LocalVariable local => local.Type ?? appContext?.SystemTypes.SystemObjectType,
            FieldReference field => field.Field.FieldType,
            ArrayAccess { Array.Type: SzArrayTypeAnalysisContext array } => array.ElementType,
            _ => null
        };

    private static void CollectLocals(IOperand? operand, HashSet<LocalVariable> set)
    {
        if (operand == null) return;
        switch (operand)
        {
            case LocalVariable lv:
                set.Add(lv);
                break;
            case FieldReference fr:
                if (fr.Local != null) set.Add(fr.Local);
                break;
            case MemoryOperand mo:
                CollectLocals(mo.Base, set);
                CollectLocals(mo.Index, set);
                break;
            case ArrayAccess aa:
                if (aa.Array != null) set.Add(aa.Array);
                CollectLocals(aa.Index, set);
                break;
            case ArrayLength al:
                if (al.Array != null) set.Add(al.Array);
                break;
            case AddressOf ao:
                CollectLocals(ao.Target, set);
                break;
            case IEnumerable<IOperand> list:
                foreach (var op in list) CollectLocals(op, set);
                break;
        }
    }

    private static CilLocalVariable GetOrCreateLocal(LocalVariable local, MethodDefinition method, Dictionary<LocalVariable, CilLocalVariable> locals)
    {
        if (locals.TryGetValue(local, out var ilLocal))
            return ilLocal;

        TypeSignature ilType;
        if (local.Type != null && local.Type.FullName != "System.Void")
            ilType = local.Type.ToTypeSignature();
        else
            ilType = method.DeclaringModule!.CorLibTypeFactory.Object;

        ilLocal = new CilLocalVariable(ilType);
        method.CilMethodBody!.LocalVariables.Add(ilLocal);
        locals[local] = ilLocal;
        return ilLocal;
    }

    private static void LoadLocal(LocalVariable local, MethodDefinition method, Dictionary<LocalVariable, CilLocalVariable> locals)
    {
        var instructions = method.CilMethodBody!.Instructions;

        if (local.IsThis)
        {
            instructions.Add(CilOpCodes.Ldarg_0);
            return;
        }

        var parameter = method.Parameters.FirstOrDefault(p => p.Name == local.Name);

        if (parameter != null)
            instructions.Add(CilOpCodes.Ldarg, parameter);
        else
            instructions.Add(CilOpCodes.Ldloc, GetOrCreateLocal(local, method, locals));
    }

    private static void StoreToOperand(IOperand operand, MethodDefinition method,
        Dictionary<LocalVariable, CilLocalVariable> locals, IMethodDescriptor writeLine,
        TypeAnalysisContext? valueTypeOnStack = null)
    {
        var instructions = method.CilMethodBody!.Instructions;

        switch (operand)
        {
            case LocalVariable local:
                var targetLocal = GetOrCreateLocal(local, method, locals);
                if (targetLocal.VariableType.FullName == "System.Object"
                    && valueTypeOnStack is { IsValueType: true } valType)
                {
                    instructions.Add(CilOpCodes.Box, valType.ToTypeSignature().ToTypeDefOrRef());
                }
                instructions.Add(CilOpCodes.Stloc, targetLocal);
                break;

            case FieldReference field:
                var fieldDescriptor = field.Field.ToFieldDescriptor();

                if (field.Field.IsStatic)
                {
                    instructions.Add(CilOpCodes.Stsfld, fieldDescriptor);
                    break;
                }

                // stfld wants the object underneath the value, but the value is already on the stack, so
                // park it in a temporary while we load the object.
                var scratch = new CilLocalVariable(fieldDescriptor.Signature!.FieldType);
                method.CilMethodBody!.LocalVariables.Add(scratch);

                instructions.Add(CilOpCodes.Stloc, scratch);
                LoadLocal(field.Local, method, locals);
                instructions.Add(CilOpCodes.Ldloc, scratch);
                instructions.Add(CilOpCodes.Stfld, fieldDescriptor);
                break;

            case ArrayAccess arrayAccess:
                // stelem needs array and index before the value, so the same trick as stfld
                var elementType = ((SzArrayTypeAnalysisContext)arrayAccess.Array.Type!).ElementType;
                var elementScratch = new CilLocalVariable(elementType.ToTypeSignature());
                method.CilMethodBody!.LocalVariables.Add(elementScratch);

                instructions.Add(CilOpCodes.Stloc, elementScratch);
                LoadLocal(arrayAccess.Array, method, locals);
                LoadOperand(arrayAccess.Index, method, locals, writeLine);
                instructions.Add(CilOpCodes.Ldloc, elementScratch);
                instructions.Add(CilOpCodes.Stelem, elementType.ToTypeSignature().ToTypeDefOrRef());
                break;

            case MemoryOperand memory:
                if (memory.Base is LocalVariable memArrLocal && memArrLocal.Type is SzArrayTypeAnalysisContext memArrType && memory.Addend >= 0x20)
                {
                    var elemType = memArrType.ElementType;
                    var elemSize = elemType.IsValueType ? Math.Max(1, (int)TypeSizes.UnboxedSize(elemType, 8)) : 8;
                    var elemIdx = (int)((memory.Addend - 0x20) / elemSize);
                    var memScratch = new CilLocalVariable(elemType.ToTypeSignature());
                    method.CilMethodBody!.LocalVariables.Add(memScratch);
                    instructions.Add(CilOpCodes.Stloc, memScratch);
                    LoadLocal(memArrLocal, method, locals);
                    instructions.Add(CilOpCodes.Ldc_I4, elemIdx);
                    instructions.Add(CilOpCodes.Ldloc, memScratch);
                    instructions.Add(CilOpCodes.Stelem, elemType.ToTypeSignature().ToTypeDefOrRef());
                    break;
                }
                if (memory.Index == null && memory.Addend == 0 && memory.Scale == 0
                    && memory.Base is LocalVariable local2)
                {
                    // Can pointer assignments just be ignored because it's C#? (Move [local], 123)
                    instructions.Add(CilOpCodes.Stloc, GetOrCreateLocal(local2, method, locals));
                    break;
                }
                instructions.Add(CilOpCodes.Pop);
                break;

            default:
                instructions.Add(CilOpCodes.Ldstr, Diagnostic($"Store into unknown operand: {operand}"));
                instructions.Add(CilOpCodes.Call, writeLine);
                instructions.Add(CilOpCodes.Pop);
                break;
        }
    }
}
