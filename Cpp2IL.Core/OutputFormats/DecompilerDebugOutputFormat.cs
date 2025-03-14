using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public struct InstructionIndex(ulong address) : IOperand
    {
        public OperandType Type => OperandType.Int;
        public int Size { get; set; }
        public ulong Address = address;
    }

    public override string OutputFormatId => "decompiler-debug";
    public override string OutputFormatName => "Output format to debug/test the decompiler";

    private static ConcurrentDictionary<string, int> _registerNumbers = [];
    private ModuleDefinition _module;

    private Decompiler.Decompiler _decompiler = new();

    public static int SuccessCount;
    public static int TotalCount;

    private int _maxInstructionCount = 3000;

    private static bool _dontActuallyWriteFiles = false;

    private static readonly InstructionSetIndependentOperand IsilCarryFlag = InstructionSetIndependentOperand.MakeRegister("cf");
    private static readonly InstructionSetIndependentOperand IsilOverflowFlag = InstructionSetIndependentOperand.MakeRegister("of");
    private static readonly InstructionSetIndependentOperand IsilSignFlag = InstructionSetIndependentOperand.MakeRegister("sf");
    private static readonly InstructionSetIndependentOperand IsilZeroFlag = InstructionSetIndependentOperand.MakeRegister("zf");
    private static readonly InstructionSetIndependentOperand IsilParityFlag = InstructionSetIndependentOperand.MakeRegister("pf");
    private static readonly InstructionSetIndependentOperand IsilTempRegister = InstructionSetIndependentOperand.MakeRegister("tmp");
    private static readonly InstructionSetIndependentOperand IsilXmm0Register = InstructionSetIndependentOperand.MakeRegister("xmm0");
    private static readonly InstructionSetIndependentOperand IsilRaxRegister = InstructionSetIndependentOperand.MakeRegister("rax");

    // cached registers
    private static IOperand _carryFlag;
    private static IOperand _overflowFlag;
    private static IOperand _signFlag;
    private static IOperand _zeroFlag;
    private static IOperand _parityFlag;
    private static IOperand _tempRegister;
    private static IOperand _xmm0Register;
    private static IOperand _raxRegister;

    public override void OnOutputFormatSelected()
    {
        base.OnOutputFormatSelected();
        NoParallel = true; // parallel makes it fail often

        _carryFlag = TranslateOperand(IsilCarryFlag);
        _overflowFlag = TranslateOperand(IsilOverflowFlag);
        _signFlag = TranslateOperand(IsilSignFlag);
        _zeroFlag = TranslateOperand(IsilZeroFlag);
        _parityFlag = TranslateOperand(IsilParityFlag);
        _tempRegister = TranslateOperand(IsilTempRegister);
        _xmm0Register = TranslateOperand(IsilXmm0Register);
        _raxRegister = TranslateOperand(IsilRaxRegister);
    }

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (methodContext.FullName.StartsWith("UnityEngine.") || methodContext.FullName.StartsWith("System.")) return;

        if (!methodDefinition.IsManagedMethodWithBody()) return;
        methodDefinition.CilMethodBody = new CilMethodBody(methodDefinition);
        _module = methodDefinition.Module!;

        Interlocked.Increment(ref TotalCount);
        Logger.InfoNewline($"Decompiling {methodContext.FullName}...", "Decompiler Debug");

        try
        {
            var isil = methodContext.AppContext.InstructionSet.GetIsilFromMethod(methodContext);

            if (isil.Count > _maxInstructionCount)
            {
                Logger.WarnNewline($"Too many instructions in {methodContext.FullName} ({isil.Count}), skipping", "Decompiler Debug");
                return;
            }

            var decompilerIl = TranslateIsilToDecompilerIl(isil, methodDefinition, methodContext);

            var isilParams = X64CallingConventionResolver.ResolveForManaged(methodContext);
            var decompilerParams = isilParams.Select(o => TranslateOperand(o)).ToList();

            var method = new Method(methodDefinition, decompilerIl, decompilerParams);
            method.ControlFlowGraph.RemoveNops();
            _decompiler.Decompile(method);

            var outputPath = Path.Combine(Path.GetDirectoryName(Environment.CurrentDirectory)!, "CFG-Output");
            WriteControlFlowGraph(method.ControlFlowGraph, methodContext, outputPath);

            Interlocked.Increment(ref SuccessCount);
        }
        catch (Exception e)
        {
            Decompiler.Decompiler.ReplaceBodyWithException(methodDefinition, "Decompilation failed: " + e);
            Logger.ErrorNewline(e.ToString(), "Decompiler Debug");
        }
    }

    private static void WriteControlFlowGraph(ControlFlowGraph graph, MethodAnalysisContext method, string outputPath)
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
                               		"label"="{(isEntry ? "Entry" : "Exit")} ({block.Id})"
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
        var typePath =
            Path.Combine(method.DeclaringType!.FullName.Split('.').Select(MiscUtils.CleanPathElement).ToArray());
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

        if (!_dontActuallyWriteFiles)
        {
            var directory = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, sb.ToString());
        }
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

            // when it's memory, write should be used instead of move
            var moveOp = OpCode.Nop;
            if (instruction.Operands.Length > 0)
                moveOp = instruction.Operands[0].Type == InstructionSetIndependentOperand.OperandType.Memory
                    ? OpCode.Write
                    : OpCode.Move;

            OpCode opCode;

            var operands = instruction.Operands.Select(o => TranslateOperand(o)).ToList();
            // normally memory operands have read instruction but when writing to memory this is used
            var operandsNoRead = instruction.Operands.Select(o => TranslateOperand(o, false)).ToList();

            // using -1 for index here should actually be fine
            switch (instruction.OpCode.Mnemonic)
            {
                case IsilMnemonic.Move:
                    Add(new Instruction(-1, moveOp, operandsNoRead[0], operands[1]), instruction);
                    break;

                case IsilMnemonic.LoadAddress:
                    Add(new Instruction(-1, moveOp, operandsNoRead[0], operandsNoRead[1]), instruction);
                    break;

                case IsilMnemonic.Call:
                case IsilMnemonic.CallNoReturn:
                    if (instruction.Operands[0].Data is IsilRegisterOperand)
                    {
                        Add(new Instruction(-1, OpCode.Unknown, new StringOp($"Indirect call: {instruction}")),
                            instruction);
                        break;
                    }

                    // If it's last instruction then it's tail call
                    var isTailCall = i == isil.Count - 1;

                    // Call -> interrupt
                    if (!isTailCall)
                        isTailCall = isil[i + 1].OpCode.Mnemonic == IsilMnemonic.Interrupt;

                    opCode = isTailCall ? OpCode.TailCall : OpCode.Call;

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
                        Add(
                            new Instruction(-1, OpCode.Unknown,
                                new StringOp($"Method not found at {address:X}")), instruction);
                        break;
                    }

                    var definition = calledMethod.ToMethodDescriptor(methodDefinition.Module!).Resolve()!;

                    IOperand? returnValue = null;
                    if (calledMethod.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                        returnValue = _xmm0Register;
                    else if (!calledMethod.IsVoid)
                        returnValue = _raxRegister;

                    var callInfo = new CallInfo(definition, operands.Skip(1).ToList());
                    var callInstruction = new Instruction(-1, opCode, callInfo);

                    Add(returnValue == null
                            ? callInstruction
                            : new Instruction(-1, OpCode.Move, returnValue, callInstruction),
                        instruction);
                    break;

                case IsilMnemonic.Exchange:
                    // tmp = b, b = a, a = tmp
                    Add(new Instruction(-1, OpCode.Move, _tempRegister, operands[1]), instruction);
                    Add(new Instruction(-1, moveOp, operandsNoRead[1], operands[0]), instruction);
                    Add(new Instruction(-1, moveOp, operandsNoRead[0], _tempRegister), instruction);
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

                    Add(new Instruction(-1, moveOp, operandsNoRead[0],
                        new Instruction(-1, opCode, operands[1], operands[2])), instruction);
                    break;

                case IsilMnemonic.ShiftLeft:
                case IsilMnemonic.ShiftRight:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.ShiftRight => OpCode.ShiftRight,
                        IsilMnemonic.ShiftLeft => OpCode.ShiftLeft,
                    };

                    Add(new Instruction(-1, moveOp, operandsNoRead[0],
                        new Instruction(-1, opCode, operands[0], operands[1])), instruction);
                    break;

                case IsilMnemonic.Not:
                case IsilMnemonic.Neg:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.Not => OpCode.Not,
                        IsilMnemonic.Neg => OpCode.Negate
                    };

                    Add(new Instruction(-1, moveOp, operandsNoRead[0],
                        new Instruction(-1, opCode, operands[0])), instruction);
                    break;

                case IsilMnemonic.Compare: // set flags
                    // tmp = a - b
                    Add(
                        new Instruction(-1, OpCode.Move, _tempRegister,
                            new Instruction(-1, OpCode.Subtract, operands[0], operands[1])), instruction);
                    // CF = a < b
                    Add(
                        new Instruction(-1, OpCode.Move, _carryFlag,
                            new Instruction(-1, OpCode.CheckLess, operands[0], operands[1])), instruction);
                    // OF = tmp > a
                    Add(
                        new Instruction(-1, OpCode.Move, _overflowFlag,
                            new Instruction(-1, OpCode.CheckGreater, _tempRegister, operands[0])), instruction);
                    // SF = tmp < 0
                    Add(
                        new Instruction(-1, OpCode.Move, _signFlag,
                            new Instruction(-1, OpCode.CheckLess, _tempRegister, new IntOp(0))), instruction);
                    // ZF = tmp == 0
                    Add(
                        new Instruction(-1, OpCode.Move, _zeroFlag,
                            new Instruction(-1, OpCode.CheckEqual, _tempRegister, new IntOp(0))), instruction);
                    // PF = tmp & 1
                    Add(
                        new Instruction(-1, OpCode.Move, _parityFlag,
                            new Instruction(-1, OpCode.And, _tempRegister, new IntOp(1))), instruction);
                    break;

                case IsilMnemonic.ShiftStack:
                    Add(new Instruction(-1, OpCode.ShiftStack, operands[0]), instruction);
                    break;

                case IsilMnemonic.Push:
                case IsilMnemonic.Pop:
                    Add(
                        new Instruction(-1, OpCode.Unknown,
                            new StringOp($"Somehow Cpp2IL didn't translate {instruction} to ISIL!")), instruction);
                    break;

                case IsilMnemonic.Return:
                    Add(
                        instruction.Operands.Length == 0
                            ? new Instruction(-1, OpCode.Return)
                            : new Instruction(-1, OpCode.Return, operands[0]),
                        instruction);
                    break;

                case IsilMnemonic.Goto:
                    Add(new Instruction(-1, OpCode.Jump, operands[0]), instruction);
                    break;

                case IsilMnemonic.JumpIfEqual:
                    // ZF = 1
                    Add(
                        new Instruction(-1, OpCode.ConditionalJump, operands[0], _zeroFlag), instruction);
                    break;

                case IsilMnemonic.JumpIfNotEqual:
                    // ZF = 0
                    Add(
                        new Instruction(-1, OpCode.ConditionalJump, operands[0],
                            new Instruction(-1, OpCode.Not, _zeroFlag)), instruction);
                    break;

                case IsilMnemonic.JumpIfGreater:
                    // ZF = 0 & SF = OF
                    var zfIs0 = new Instruction(-1, OpCode.Not, _zeroFlag);
                    var sfIsOf = new Instruction(-1, OpCode.CheckEqual, _signFlag, _overflowFlag);
                    Add(
                        new Instruction(-1, OpCode.ConditionalJump, operands[0],
                            new Instruction(-1, OpCode.And, zfIs0, sfIsOf)), instruction);
                    break;

                case IsilMnemonic.JumpIfGreaterOrEqual:
                    // SF = OF
                    var sfIsOf2 = new Instruction(-1, OpCode.CheckEqual, _signFlag, _overflowFlag);
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], sfIsOf2), instruction);
                    break;

                case IsilMnemonic.JumpIfLess:
                    // SF != OF
                    var sfIsNotOf = new Instruction(-1, OpCode.Not,
                        new Instruction(-1, OpCode.CheckEqual, _signFlag, _overflowFlag));
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], sfIsNotOf), instruction);
                    break;

                case IsilMnemonic.JumpIfLessOrEqual:
                    // ZF = 1 | SF != OF
                    var sfIsNotOf2 = new Instruction(-1, OpCode.Not,
                        new Instruction(-1, OpCode.CheckEqual, _signFlag, _overflowFlag));
                    Add(
                        new Instruction(-1, OpCode.ConditionalJump, operands[0],
                            new Instruction(-1, OpCode.Or, sfIsNotOf2, _zeroFlag)), instruction);
                    break;

                case IsilMnemonic.JumpIfSign:
                    // SF = 1
                    Add(new Instruction(-1, OpCode.ConditionalJump, operands[0], _signFlag), instruction);
                    break;

                case IsilMnemonic.JumpIfNotSign:
                    // SF = 0
                    Add(
                        new Instruction(-1, OpCode.ConditionalJump, operands[0],
                            new Instruction(-1, OpCode.Not, _signFlag)), instruction);
                    break;

                case IsilMnemonic.SignExtend:
                    // IsilMnemonic.SignExtend is not used anywhere
                    Add(
                        new Instruction(-1, OpCode.Unknown,
                            new StringOp($"SignExtend is not implemented ({instruction})")), instruction);
                    break;

                case IsilMnemonic.Interrupt:
                    // Should interrupt return?
                    if (methodContext.IsVoid)
                        Add(new Instruction(-1, OpCode.Return), instruction);
                    else if (methodContext.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                        Add(new Instruction(-1, OpCode.Return, _xmm0Register), instruction);
                    else
                        Add(new Instruction(-1, OpCode.Return, _raxRegister), instruction);
                    break;

                case IsilMnemonic.Nop:
                    // these could be branch targets so these need to be added
                    Add(new Instruction(-1, OpCode.Nop), instruction);
                    break;

                case IsilMnemonic.NotImplemented:
                case IsilMnemonic.Invalid:
                    Add(new Instruction(-1, OpCode.Unknown, operands[0]), instruction);
                    break;

                default:
                    Add(new Instruction(-1, OpCode.Unknown, new StringOp($"Unknown instruction: {instruction}")),
                        instruction);
                    break;
            }
        }

        // fix ranches
        foreach (var instruction in instructions)
        {
            if (instruction.Operands.Count == 0) continue;

            if (instruction.Operands[0] is InstructionIndex target)
            {
                // try because it could be a tail call or something weird
                try
                {
                    instruction.Operands[0] = new BranchTargetInstruction(addressMap.First(i => i.Item1 == target.Address).Item2);
                }
                catch (Exception e)
                {
                    instruction.OpCode = OpCode.Unknown;
                    instruction.Operands =
                        [new StringOp($"Branch target not found: @{target.Address:X}")];
                }
            }
        }

        // fix indexes
        for (var i = 0; i < instructions.Count; i++)
            instructions[i].Index = i;

        return instructions;

        void Add(Instruction newInstruction, InstructionSetIndependentInstruction instruction)
        {
            addressMap.Add((instruction.ActualAddress, newInstruction));
            instructions.Add(newInstruction);
        }
    }

    private static IOperand TranslateOperand(InstructionSetIndependentOperand operand, bool addReadInstruction = true)
    {
        switch (operand.Data)
        {
            case IsilImmediateOperand immediate:
                switch (immediate.Value)
                {
                    case int num:
                        return new IntOp(num);
                    // X86InstructionSet sometimes uses MaxValue
                    case ushort and ushort.MaxValue:
                    case uint and uint.MaxValue:
                    case ulong and ulong.MaxValue:
                        return new IntOp(int.MaxValue);
                    case ulong num2:
                        return new LongOp((int)num2);
                    case string text:
                        return new StringOp(text);
                }

                break;

            case IsilStackOperand stackOffset:
                return new StackOffset(stackOffset.Offset);

            case IsilRegisterOperand register:
            {
                if (!_registerNumbers.ContainsKey(register.RegisterName))
                    _registerNumbers[register.RegisterName] = _registerNumbers.Count;

                var number = _registerNumbers[register.RegisterName];

                return new Register(number, register.RegisterName);
            }
            case IsilMemoryOperand memory:
                IOperand? newOperand = null;
                var needsPlus = false;

                if (memory.Base != null)
                {
                    newOperand = TranslateOperand((InstructionSetIndependentOperand)memory.Base);
                    needsPlus = true;
                }

                if (memory.Addend != 0)
                {
                    if (needsPlus)
                    {
                        var opCode = memory.Addend > 0 ? OpCode.Add : OpCode.Subtract;
                        newOperand = new Instruction(-1, opCode, newOperand, new LongOp(memory.Addend));
                    }
                    else
                    {
                        newOperand = new LongOp(memory.Addend);
                    }

                    needsPlus = true;
                }

                if (memory.Index != null)
                {
                    if (needsPlus)
                    {
                        newOperand = new Instruction(-1, OpCode.Add, newOperand,
                            TranslateOperand((InstructionSetIndependentOperand)memory.Index));
                    }
                    else
                    {
                        newOperand = TranslateOperand((InstructionSetIndependentOperand)memory.Index);
                    }

                    if (memory.Scale > 1)
                    {
                        newOperand = new Instruction(-1, OpCode.Multiply, newOperand, new IntOp(memory.Scale));
                    }
                }

                // move destination shouldn't be read instruction
                if (addReadInstruction)
                    newOperand = new Instruction(-1, OpCode.Read, newOperand);

                return newOperand!;

            case InstructionSetIndependentInstruction instruction:
                return new InstructionIndex(instruction.ActualAddress);

            case IsilVectorRegisterElementOperand vectorRegisterElement:
                return TranslateOperand(InstructionSetIndependentOperand.MakeRegister(vectorRegisterElement.RegisterName));
        }

        return new StringOp($"Unknown operand: {operand}");
    }
}
