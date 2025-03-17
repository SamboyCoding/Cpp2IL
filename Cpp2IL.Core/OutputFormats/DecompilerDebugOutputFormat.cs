using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using Decompiler;
using Decompiler.ControlFlow;
using Decompiler.IL;
using LibCpp2IL.BinaryStructures;
using Logger = Cpp2IL.Core.Logging.Logger;

namespace Cpp2IL.Core.OutputFormats;

// i know, there is a lot of platform specific stuff in here
public class DecompilerDebugOutputFormat : AsmResolverDllOutputFormat
{
    [DebuggerDisplay("Address = {Address}")]
    private struct InstructionAddress(ulong address)
    {
        public ulong Address = address;
        public override string ToString() => $"@{Address}";
    }

    public override string OutputFormatId => "decompiler-debug";
    public override string OutputFormatName => "Output format to debug/test the decompiler";

    private static ConcurrentDictionary<string, int> _registerNumbers = [];

    private Decompiler.Decompiler _decompiler = new();

    public static int SuccessCount;
    public static int TotalCount;

    private static int _maxMethodSize = 50_000;

    // cached registers
    private static object _carryFlag;
    private static object _overflowFlag;
    private static object _signFlag;
    private static object _zeroFlag;
    private static object _parityFlag;
    private static object _xmm0;
    private static object _rax;

    public override void OnOutputFormatSelected()
    {
        base.OnOutputFormatSelected();
        NoParallel = true; // parallel makes it fail often

        _carryFlag = CreateRegister("cf");
        _overflowFlag = CreateRegister("of");
        _signFlag = CreateRegister("sf");
        _zeroFlag = CreateRegister("zf");
        _parityFlag = CreateRegister("pf");
        _xmm0 = CreateRegister("xmm0");
        _rax = CreateRegister("rax");
    }

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (methodContext.FullName.StartsWith("UnityEngine.")
            || methodContext.FullName.StartsWith("Unity.")
            || methodContext.FullName.StartsWith("Mono.")
            || methodContext.FullName.StartsWith("System.")) return;

        if (!methodDefinition.IsManagedMethodWithBody()) return;
        methodDefinition.CilMethodBody = new CilMethodBody(methodDefinition);

        Interlocked.Increment(ref TotalCount);
        Logger.InfoNewline($"Decompiling {methodContext.FullName}...", "Decompiler Debug");

        try
        {
            if (_maxMethodSize != -1 && methodContext.RawBytes.Length > _maxMethodSize)
                throw new LimitReachedException($"Too big method body in {methodContext.DeclaringType!.Name}.{methodContext.Name}! ({methodContext.RawBytes.Length} bytes)");

            var isil = methodContext.AppContext.InstructionSet.GetIsilFromMethod(methodContext);
            var il = TranslateIsilToDecompilerIl(isil, methodDefinition, methodContext);

            var isilParams = X64CallingConventionResolver.ResolveForManaged(methodContext);
            var ilParams = isilParams.Select(ConvertOperand).ToList();

            var method = new Method(methodDefinition, il, ilParams);
            _decompiler.Decompile(method);

            var outputPath = Path.Combine(Path.GetDirectoryName(Environment.CurrentDirectory)!, "CFG-Output");
            WriteControlFlowGraph(method.ControlFlowGraph, methodContext, methodDefinition, method, outputPath);

            Interlocked.Increment(ref SuccessCount);
        }
        catch (LimitReachedException e)
        {
            Logger.ErrorNewline(e.ToString(), "Decompiler Debug");
        }
    }

    private static void WriteControlFlowGraph(ControlFlowGraph graph, MethodAnalysisContext method, MethodDefinition definition, Method decompilerMethod, string outputPath)
    {
        var sb = new StringBuilder();
        var edges = new List<(int, int)>();

        sb.AppendLine("digraph ControlFlowGraph {");
        sb.AppendLine("    \"label\"=\"Control flow graph\"");

        foreach (var block in graph.Blocks)
        {
            if (block == graph.EntryBlock || block == graph.ExitBlock)
            {
                var isEntry = block == graph.EntryBlock;
                sb.AppendLine($"""
                               	{block.Id} [
                               		"color"="{(isEntry ? "green" : "red")}"
                               		"label"="{(isEntry ? $@"Entry\n{definition}\nParams: {string.Join(", ", decompilerMethod.ParameterLocals)}\n" : "Exit")} ({block.Id})"
                               	]
                               """);
            }
            else
            {
                sb.AppendLine($"""
                               	{block.Id} [
                               		"shape"="box"
                               		"label"="{block.ToString().Replace("\"", "\\\"").Replace("\n", "\\n")}"
                               	]
                               """);
            }

            edges.AddRange(block.Successors.Select(b => (block.Id, b.Id)));
        }

        foreach (var edge in edges)
            sb.AppendLine($"    {edge.Item1} -> {edge.Item2}");

        sb.AppendLine("}");

        var assemblyName = MiscUtils.CleanPathElement(method.DeclaringType!.DeclaringAssembly.CleanAssemblyName);
        var typePath = Path.Combine(method.DeclaringType!.FullName.Split('.').Select(MiscUtils.CleanPathElement).ToArray());
        var directoryPath = Path.Combine(outputPath, assemblyName, typePath);
        var methodName = MiscUtils.CleanPathElement(method.Name + "_" + string.Join("_",
            method.Parameters.Select(p => MiscUtils.CleanPathElement(p.ParameterTypeContext.Name))));
        var path = Path.Combine(directoryPath, methodName) + ".dot";

        // Too long
        if (path.Length > 260)
        {
            path = path[..250];
            path += ".dot";
        }

        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, sb.ToString());
    }

    private static List<Instruction> TranslateIsilToDecompilerIl(List<InstructionSetIndependentInstruction> isil,
        MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        var addressMap = new List<(ulong, Instruction)>();
        var instructions = new List<Instruction>();
        var appContext = methodContext.AppContext;

        for (var i = 0; i < isil.Count; i++)
        {
            var instruction = isil[i];

            OpCode opCode;
            var operands = instruction.Operands.Select(ConvertOperand).ToList();

            // Using -1 for index here should actually be fine
            switch (instruction.OpCode.Mnemonic)
            {
                case IsilMnemonic.Move:
                case IsilMnemonic.LoadAddress:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.Move => OpCode.Move,
                        IsilMnemonic.LoadAddress => OpCode.LoadAddress
                    };

                    Add(new Instruction(-1, opCode, operands[0], operands[1]), instruction);
                    break;
                case IsilMnemonic.ShiftLeft:
                case IsilMnemonic.ShiftRight:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.ShiftRight => OpCode.ShiftRight,
                        IsilMnemonic.ShiftLeft => OpCode.ShiftLeft
                    };

                    Add(new Instruction(-1, opCode, operands[0], operands[0], operands[1]), instruction);
                    break;

                case IsilMnemonic.Call:
                case IsilMnemonic.CallNoReturn:
                    if (instruction.Operands[0].Data is IsilRegisterOperand)
                    {
                        Add(new Instruction(-1, OpCode.Unknown, $"Indirect call: {instruction}"), instruction);
                        break;
                    }

                    var address = ((ulong)((IsilImmediateOperand)instruction.Operands[0].Data).Value);
                    MethodAnalysisContext? calledMethod = null;

                    if (appContext.MethodsByAddress.TryGetValue(address, out var possibleMethods))
                    {
                        if (possibleMethods.Count == 1)
                        {
                            calledMethod = possibleMethods[0];
                        }
                        else
                        {
                            var lpars = -1;

                            foreach (var possible in possibleMethods)
                            {
                                var pars = possible.ParameterCount;
                                if (possible.IsStatic) pars++;
                                if (pars > lpars)
                                {
                                    lpars = pars;
                                    calledMethod = possible;
                                }
                            }
                        }
                    }

                    if (calledMethod == null)
                    {
                        Add(new Instruction(-1, OpCode.Unknown, $"Method not found: {address:X}"), instruction);
                        break;
                    }

                    var definition = calledMethod.ToMethodDescriptor(methodDefinition.Module!).Resolve()!;

                    object? returnValue = null;

                    if (calledMethod.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                        returnValue = _xmm0;
                    else if (!calledMethod.IsVoid)
                        returnValue = _rax;

                    opCode = returnValue == null ? OpCode.CallVoid : OpCode.Call;

                    // If it's last instruction then it's tail call
                    var isTailCall = i == isil.Count - 1;

                    // Call -> interrupt
                    if (!isTailCall)
                        isTailCall = isil[i + 1].OpCode.Mnemonic == IsilMnemonic.Interrupt;

                    if (isTailCall)
                    {
                        if (opCode == OpCode.Call)
                            opCode = OpCode.TailCall;

                        if (opCode == OpCode.CallVoid)
                            opCode = OpCode.TailCallVoid;
                    }

                    var call = new Instruction(-1, opCode, definition);

                    if (returnValue != null)
                        call.Operands.Insert(0, returnValue);

                    foreach (var arg in operands.Skip(1))
                        call.Operands.Add(arg);

                    Add(call, instruction);
                    break;

                case IsilMnemonic.Exchange:
                    // tmp = b, b = a, a = tmp
                    var exchangeTemp = CreateRegister("exchangeTemp");

                    Add(new Instruction(-1, OpCode.Move, exchangeTemp, operands[1]), instruction); // tmp = b
                    Add(new Instruction(-1, OpCode.Move, operands[1], operands[0]), instruction); // b = a
                    Add(new Instruction(-1, OpCode.Move, operands[0], exchangeTemp), instruction); // a = tmp
                    break;

                case IsilMnemonic.Add:
                case IsilMnemonic.Subtract:
                case IsilMnemonic.Multiply:
                case IsilMnemonic.Divide:
                case IsilMnemonic.And:
                case IsilMnemonic.Or:
                case IsilMnemonic.Xor:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.Add => OpCode.Add,
                        IsilMnemonic.Subtract => OpCode.Subtract,
                        IsilMnemonic.Multiply => OpCode.Multiply,
                        IsilMnemonic.Divide => OpCode.Divide,
                        IsilMnemonic.And => OpCode.And,
                        IsilMnemonic.Or => OpCode.Or,
                        IsilMnemonic.Xor => OpCode.Xor
                    };

                    Add(new Instruction(-1, opCode, operands[0], operands[1], operands[2]), instruction);
                    break;

                case IsilMnemonic.Not:
                case IsilMnemonic.Neg:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.Not => OpCode.Not,
                        IsilMnemonic.Neg => OpCode.Negate
                    };

                    Add(new Instruction(-1, opCode, operands[0], operands[0]), instruction);
                    break;

                case IsilMnemonic.ShiftStack:
                case IsilMnemonic.Goto:
                case IsilMnemonic.Invalid:
                case IsilMnemonic.NotImplemented:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.ShiftStack => OpCode.ShiftStack,
                        IsilMnemonic.Goto => OpCode.Jump,
                        IsilMnemonic.Invalid => OpCode.Unknown,
                        IsilMnemonic.NotImplemented => OpCode.Unknown
                    };

                    Add(new Instruction(-1, opCode, operands[0]), instruction);
                    break;

                case IsilMnemonic.Compare: // Set flags
                    var cmpA = operands[0];
                    var cmpB = operands[1];

                    var cmpTemp = CreateRegister("cmpTemp");

                    // cmpTemp = a - b
                    Add(new Instruction(-1, OpCode.Subtract, cmpTemp, cmpA, cmpB), instruction);
                    // CF = a < b
                    Add(new Instruction(-1, OpCode.CheckLess, _carryFlag, cmpA, cmpB), instruction);
                    // OF = cmpTemp > a
                    Add(new Instruction(-1, OpCode.CheckGreater, _overflowFlag, cmpTemp, cmpA), instruction);
                    // SF = cmpTemp < 0
                    Add(new Instruction(-1, OpCode.CheckLess, _signFlag, cmpTemp, 0), instruction);
                    // ZF = cmpTemp == 0
                    Add(new Instruction(-1, OpCode.CheckEqual, _zeroFlag, cmpTemp, 0), instruction);
                    // PF = cmpTemp & 1
                    Add(new Instruction(-1, OpCode.And, _parityFlag, cmpTemp, 1), instruction);
                    break;

                case IsilMnemonic.Push:
                case IsilMnemonic.Pop:
                    Add(new Instruction(-1, OpCode.Unknown, $"Somehow Cpp2IL didn't translate {instruction} to ISIL!"), instruction);
                    break;

                case IsilMnemonic.Return:
                    Add(
                        instruction.Operands.Length == 0
                            ? new Instruction(-1, OpCode.ReturnVoid)
                            : new Instruction(-1, OpCode.Return, operands[0]),
                        instruction);
                    break;

                case IsilMnemonic.JumpIfEqual:
                    // ZF = 1
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], _zeroFlag), instruction);
                    break;

                case IsilMnemonic.JumpIfNotEqual:
                    // ZF = 0
                    Add(new Instruction(-1, OpCode.Not, CreateRegister("notZf"), _zeroFlag), instruction); // notZf = !zf
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("notZf")), instruction);
                    break;

                case IsilMnemonic.JumpIfGreater:
                    // ZF = 0 & SF = OF
                    var zfIsZero = CreateRegister("zfIsZero");
                    Add(new Instruction(-1, OpCode.Not, zfIsZero, _zeroFlag), instruction); // zfIsZero = !zf
                    Add(new Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsOf"), _signFlag, _overflowFlag), instruction); // sfIsOf = sf == of
                    Add(new Instruction(-1, OpCode.And, zfIsZero, CreateRegister("sfIsOf"), zfIsZero), instruction); // zfIsZero &= sfIsOf
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], zfIsZero), instruction);
                    break;

                case IsilMnemonic.JumpIfGreaterOrEqual:
                    // SF = OF
                    Add(new Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsOf"), _signFlag, _overflowFlag), instruction); // sfIsOf = sf == of
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("sfIsOf")), instruction);
                    break;

                case IsilMnemonic.JumpIfLess:
                    // SF != OF
                    Add(new Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsNotOf"), _signFlag, _overflowFlag), instruction); // sfIsNotOf = sf == of
                    Add(new Instruction(-1, OpCode.Not, CreateRegister("sfIsNotOf"), CreateRegister("sfIsNotOf")), instruction); // sfIsNotOf = !sfIsNotOf
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("sfIsNotOf")), instruction);
                    break;

                case IsilMnemonic.JumpIfLessOrEqual:
                    // ZF = 1 | SF != OF
                    Add(new Instruction(-1, OpCode.Move, CreateRegister("zfCopy"), _zeroFlag), instruction); // zfCopy = zf
                    Add(new Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsNotOf"), _signFlag, _overflowFlag), instruction); // sfIsNotOf = sf == of
                    Add(new Instruction(-1, OpCode.Not, CreateRegister("sfIsNotOf"), CreateRegister("sfIsNotOf")), instruction); // sfIsNotOf = !sfIsNotOf
                    Add(new Instruction(-1, OpCode.Or, CreateRegister("zfCopy"), CreateRegister("sfIsNotOf"), CreateRegister("zfCopy")), instruction); // zfCopy |= sfIsNotOf
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("zfCopy")), instruction);
                    break;

                case IsilMnemonic.JumpIfSign:
                    // SF = 1
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], _signFlag), instruction);
                    break;

                case IsilMnemonic.JumpIfNotSign:
                    // SF = 0
                    Add(new Instruction(-1, OpCode.Not, CreateRegister("notSf"), _signFlag), instruction); // notSf = !sf
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("notSf")), instruction);
                    break;

                case IsilMnemonic.SignExtend:
                    // IsilMnemonic.SignExtend is not used anywhere
                    Add(new Instruction(-1, OpCode.Unknown, $"SignExtend is not implemented ({instruction})"), instruction);
                    break;

                case IsilMnemonic.Interrupt:
                    if (methodContext.IsVoid)
                        Add(new Instruction(-1, OpCode.ReturnVoid), instruction);
                    else if (methodContext.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                        Add(new Instruction(-1, OpCode.Return, _xmm0), instruction);
                    else
                        Add(new Instruction(-1, OpCode.Return, _rax), instruction);
                    break;

                case IsilMnemonic.Nop:
                    // These could be branch targets so these need to be added
                    Add(new Instruction(-1, OpCode.Nop), instruction);
                    break;

                default:
                    Add(new Instruction(-1, OpCode.Unknown, $"Unknown instruction: {instruction}"),
                        instruction);
                    break;
            }
        }

        // Fix ranches
        foreach (var instruction in instructions)
        {
            if (instruction.Operands.Count == 0) continue;

            if (instruction.Operands[0] is InstructionAddress target)
            {
                try
                {
                    instruction.Operands[0] = addressMap.First(i => i.Item1 == target.Address).Item2;
                }
                catch (Exception e)
                {
                    instruction.OpCode = OpCode.Unknown;
                    instruction.Operands = [$"Branch target not found: @{target.Address:X}"];
                }
            }
        }

        // Add return if it's not already there
        if (instructions.Count > 0)
        {
            var lastInstruction = instructions.LastOrDefault();

            if (lastInstruction != null && lastInstruction.OpCode != OpCode.Return)
            {
                if (methodContext.IsVoid)
                    instructions.Add(new Instruction(-1, OpCode.ReturnVoid));
                else if (methodContext.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                    instructions.Add(new Instruction(-1, OpCode.Return, _xmm0));
                else
                    instructions.Add(new Instruction(-1, OpCode.Return, _rax));
            }
        }

        // Fix indexes
        for (var i = 0; i < instructions.Count; i++)
            instructions[i].Index = i;

        return instructions;

        void Add(Instruction newInstruction, InstructionSetIndependentInstruction instruction)
        {
            addressMap.Add((instruction.ActualAddress, newInstruction));
            instructions.Add(newInstruction);
        }
    }

    private static object CreateRegister(string name) => ConvertOperand(InstructionSetIndependentOperand.MakeRegister(name));

    private static object ConvertOperand(InstructionSetIndependentOperand operand)
    {
        switch (operand.Data)
        {
            case IsilImmediateOperand immediate:
                return immediate.Value switch
                {
                    int num => num,
                    ushort.MaxValue => int.MaxValue,
                    uint.MaxValue or ulong.MaxValue => ulong.MaxValue,
                    ulong num2 => num2,
                    string text => text,
                    _ => $"Unknown operand: {operand}"
                };

            case IsilStackOperand stackOffset:
                return new StackOffset(stackOffset.Offset);

            case IsilRegisterOperand register:
                if (!_registerNumbers.ContainsKey(register.RegisterName))
                    _registerNumbers[register.RegisterName] = _registerNumbers.Count;

                var number = _registerNumbers[register.RegisterName];
                return new Register(number, register.RegisterName);

            case IsilMemoryOperand memory:
                Register? baseRegister = null;
                if (memory.Base != null)
                    baseRegister = (Register)ConvertOperand((InstructionSetIndependentOperand)memory.Base!);

                Register? index = null;
                if (memory.Index != null)
                    index = (Register)ConvertOperand((InstructionSetIndependentOperand)memory.Index!);

                return new MemoryAddress(baseRegister, index, memory.Addend, memory.Scale);
            case IsilVectorRegisterElementOperand vectorRegisterElement:
                return ConvertOperand(InstructionSetIndependentOperand.MakeRegister(vectorRegisterElement.RegisterName));

            case InstructionSetIndependentInstruction instruction:
                return new InstructionAddress(instruction.ActualAddress);
        }

        return $"Unknown operand: {operand}";
    }
}
