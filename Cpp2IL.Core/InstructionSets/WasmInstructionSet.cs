using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Wasm;
using WasmDisassembler;

namespace Cpp2IL.Core.InstructionSets;

public class WasmInstructionSet : Cpp2IlInstructionSet
{
    // unresolved methods get keyed well clear of real function indices
    private const ulong UnresolvedPointerBias = 0x8000_0000_0000_0000;

    // concurrent because GetIsilFromMethod runs under the parallel method fill
    private readonly ConcurrentDictionary<(string, bool, int), MethodAnalysisContext?> _mathMethods = new();

    private MethodAnalysisContext? ResolveMathMethod(ApplicationAnalysisContext app, string name, bool isDouble, int paramCount)
        => _mathMethods.GetOrAdd((name, isDouble, paramCount), key =>
        {
            var mathType = app.SystemTypes.SystemDoubleType.DeclaringAssembly.GetTypeByFullName("System.Math");
            var wantType = isDouble ? app.SystemTypes.SystemDoubleType : app.SystemTypes.SystemSingleType;

            // exact signatures only, so we never emit a call that needs an implicit numeric conversion
            return mathType?.Methods.FirstOrDefault(m =>
                m.IsStatic && m.Name == key.Item1 && m.Parameters.Count == key.Item3
                && m.Parameters.All(p => p.ParameterType == wantType));
        });

    public override BinarySlice GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context.Definition is { } methodDefinition)
        {
            var wasmDef = WasmUtils.TryGetWasmDefinition(context);

            if (wasmDef == null)
            {
                Logger.WarnNewline($"Could not find WASM definition for method {methodDefinition.HumanReadableSignature} in {methodDefinition.DeclaringType?.FullName}, probably incorrect signature calculation (signature was {WasmUtils.BuildSignature(context)}, index is {context.UnderlyingPointer})", "WasmInstructionSet");
                return BinarySlice.Empty;
            }

            if (wasmDef.AssociatedFunctionBody == null)
                throw new($"WASM definition {wasmDef}, resolved from MethodAnalysisContext {context.Definition.HumanReadableSignature} in {context.DeclaringType?.FullName} has no associated function body (signature was {WasmUtils.BuildSignature(context)}, index is {context.UnderlyingPointer})");

            return new BinarySlice(wasmDef.AssociatedFunctionBody.Instructions);
        }

        return BinarySlice.Empty;
    }

    // metadata method pointers are dynCall indices, which collide between signatures. the function space
    // index is unique and is what a wasm call actually references, so key on that instead.
    public override ulong GetPointerForMethod(MethodAnalysisContext context)
    {
        if (WasmUtils.TryGetWasmDefinition(context) is { IsImport: false } def)
            return (ulong)def.FunctionTableIndex;

        return UnresolvedPointerBias | context.UnderlyingPointer;
    }

    public override List<Instruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var def = WasmUtils.TryGetWasmDefinition(context);

        if (def?.AssociatedFunctionBody is not { } body)
            return [];

        var file = (WasmFile)context.AppContext.Binary;
        var wasmInstructions = Disassembler.Disassemble(body.Instructions, (uint)body.InstructionsOffset);

        return new WasmIsilBuilder(context, file, def, wasmInstructions, ResolveMathMethod).Build();
    }

    public override List<IOperand> GetParameterOperandsFromMethod(MethodAnalysisContext context)
    {
        if (WasmUtils.TryGetWasmDefinition(context) == null)
            return [];

        // wasm params are locals 0..n: [return buffer?] [this?] [params...] [methodinfo]
        var first = WasmUtils.HasReturnBuffer(context) ? 1 : 0;
        var count = (context.IsStatic ? 0 : 1) + context.Parameters.Count + 1; // trailing methodinfo

        return Enumerable.Range(first, count).Select(i => (IOperand)new Register(null, "L" + i)).ToList();
    }

    public override (IReadOnlyList<ulong> DataReferences, IReadOnlyList<ulong> CallTargets) InspectPotentialThrowHelper(ApplicationAnalysisContext context, ulong address)
    {
        var file = (WasmFile)context.Binary;

        if (address >= (ulong)file.FunctionTable.Count)
            return ([], []);

        var def = file.FunctionTable[(int)address];

        if (def.IsImport || def.AssociatedFunctionBody is not { } body || body.Instructions.Length > 512)
            return ([], []);

        List<WasmInstruction> instructions;
        try
        {
            instructions = Disassembler.Disassemble(body.Instructions, (uint)body.InstructionsOffset);
        }
        catch
        {
            return ([], []);
        }

        var dataReferences = new List<ulong>();
        var callTargets = new List<ulong>();

        foreach (var instruction in instructions)
        {
            // the exception type name reaches Class::FromName as an i32.const pointer into the data section
            if (instruction is { Mnemonic: WasmMnemonic.I32Const, Operands: [long value] } && value > 0)
                dataReferences.Add((ulong)value);
            else if (instruction is { Mnemonic: WasmMnemonic.Call, Operands: [ulong target] })
                callTargets.Add(target);
        }

        return (dataReferences, callTargets);
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        return new WasmKeyFunctionAddresses();
    }

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        if (context.Definition == null)
            return string.Empty;

        var def = WasmUtils.GetWasmDefinition(context);
        var disassembled = Disassembler.Disassemble(def.AssociatedFunctionBody!.Instructions, (uint)def.AssociatedFunctionBody.InstructionsOffset);

        return string.Join("\n", disassembled);
    }

    // lowers the stack machine to registers: stack slot n is Sn, local n is Ln, global n is Gn.
    private sealed class WasmIsilBuilder(MethodAnalysisContext context, WasmFile file, WasmFunctionDefinition def, List<WasmInstruction> wasmInstructions,
        Func<ApplicationAnalysisContext, string, bool, int, MethodAnalysisContext?> resolveMathMethod)
    {
        private sealed class ControlFrame
        {
            public bool IsLoop;
            public bool Dead; // opened while unreachable, tracked only for nesting
            public int EntryHeight; // stack height below the block's params
            public int ParamCount;
            public int ResultCount;
            public ulong TargetIp; // where a br to this frame ends up (the loop head or the matching end)
            public ulong EndIp;
        }

        private readonly List<Instruction> _instructions = [];
        private readonly Dictionary<ulong, int> _ipToIndex = [];
        private readonly List<ControlFrame> _frames = [];

        // registers holding a known constant, so [const + offset] loads fold to constant addresses that
        // MetadataResolver can resolve (the same job adrp tracking does on arm64). cleared at merges.
        private readonly Dictionary<string, long> _constants = [];

        private int _stackHeight;
        private bool _dead;

        private static Immediate Imm(long value) => new(value);
        private static Register Temp(string name = "TEMP") => new(null, name);

        private Register Stack(int depth) => new(null, "S" + depth);
        private Register Push() => Stack(_stackHeight++);
        private Register Pop() => Stack(--_stackHeight);

        private Instruction Add(OpCode opCode, params List<IOperand> operands)
        {
            var instruction = new Instruction(_instructions.Count, opCode, operands);
            _instructions.Add(instruction);

            if (instruction.Destination is Register written)
                _constants.Remove(written.Name);

            return instruction;
        }

        private ControlFrame FrameAt(int depth) => _frames[_frames.Count - 1 - depth];

        private (int Params, int Results) DecodeBlockType(long blockType)
        {
            if (blockType >= 0)
            {
                var entry = file.GetTypeEntry((int)blockType);
                return ((int)entry.ParamCount, (int)entry.ReturnCount);
            }

            return blockType == -0x40 ? (0, 0) : (0, 1);
        }

        public List<Instruction> Build()
        {
            var functionType = def.GetType(file);
            var returnsValue = functionType.ReturnCount > 0;
            var hasReturnBuffer = WasmUtils.HasReturnBuffer(context);

            // match every block/loop/if with its end (and else) up front, br needs forward targets
            var endIndexFor = new Dictionary<int, int>();
            var elseIndexFor = new Dictionary<int, int>();
            var openers = new Stack<int>();
            openers.Push(-1); // the function body is an implicit block

            for (var i = 0; i < wasmInstructions.Count; i++)
            {
                switch (wasmInstructions[i].Mnemonic)
                {
                    case WasmMnemonic.Block:
                    case WasmMnemonic.Loop:
                    case WasmMnemonic.If:
                        openers.Push(i);
                        break;
                    case WasmMnemonic.Else:
                        elseIndexFor[openers.Peek()] = i;
                        break;
                    case WasmMnemonic.End:
                        endIndexFor[openers.Pop()] = i;
                        break;
                }
            }

            if (!endIndexFor.TryGetValue(-1, out var functionEndIndex))
                throw new Exception("Wasm function body has no terminating end instruction");

            _frames.Add(new ControlFrame
            {
                ResultCount = (int)functionType.ReturnCount,
                TargetIp = wasmInstructions[functionEndIndex].Ip,
                EndIp = wasmInstructions[functionEndIndex].Ip
            });

            void EmitReturn()
            {
                if (!context.IsVoid && returnsValue && _stackHeight > 0)
                    Add(OpCode.Return, Stack(_stackHeight - 1));
                else if (!context.IsVoid && hasReturnBuffer)
                    Add(OpCode.Return, new Register(null, "L0"));
                else
                    Add(OpCode.Return);
            }

            // values that would be discarded by a branch get moved down to where the target expects its results
            void EmitBranchFixups(ControlFrame frame)
            {
                var arity = frame.IsLoop ? frame.ParamCount : frame.ResultCount;
                var source = _stackHeight - arity;

                if (source == frame.EntryHeight)
                    return;

                for (var j = 0; j < arity; j++)
                    Add(OpCode.Move, Stack(frame.EntryHeight + j), Stack(source + j));
            }

            // a conditional branch whose fixup moves must only happen when taken gets inverted into a skip
            void EmitConditionalBranch(IOperand condition, ControlFrame frame)
            {
                var arity = frame.IsLoop ? frame.ParamCount : frame.ResultCount;

                if (_stackHeight - arity == frame.EntryHeight || arity == 0)
                {
                    Add(OpCode.ConditionalJump, Imm((long)frame.TargetIp), condition);
                    return;
                }

                var inverse = Temp();
                Add(OpCode.CheckEqual, inverse, condition, Imm(0));
                var skip = Add(OpCode.ConditionalJump, Imm(0), inverse);
                EmitBranchFixups(frame);
                Add(OpCode.Jump, Imm((long)frame.TargetIp));
                skip.SetOperand(0, Add(OpCode.Nop));
            }

            for (var i = 0; i < wasmInstructions.Count; i++)
            {
                var insn = wasmInstructions[i];
                _ipToIndex[insn.Ip] = _instructions.Count;

                if (_dead)
                {
                    switch (insn.Mnemonic)
                    {
                        case WasmMnemonic.Block:
                        case WasmMnemonic.Loop:
                        case WasmMnemonic.If:
                            _frames.Add(new ControlFrame { Dead = true });
                            break;
                        case WasmMnemonic.Else:
                        {
                            var frame = _frames[^1];
                            if (!frame.Dead)
                            {
                                // the then-branch never falls through here, but the if's false edge does
                                _dead = false;
                                _stackHeight = frame.EntryHeight + frame.ParamCount;
                                _constants.Clear();
                            }

                            break;
                        }
                        case WasmMnemonic.End:
                        {
                            var frame = _frames[^1];
                            _frames.RemoveAt(_frames.Count - 1);

                            if (!frame.Dead)
                            {
                                _dead = false;
                                _stackHeight = frame.EntryHeight + frame.ResultCount;
                                _constants.Clear();
                                Add(OpCode.Nop); // branch target
                            }

                            break;
                        }
                    }

                    continue;
                }

                switch (insn.Mnemonic)
                {
                    case WasmMnemonic.Unreachable:
                        Add(OpCode.Interrupt);
                        _dead = true;
                        break;
                    case WasmMnemonic.Nop:
                        Add(OpCode.Nop);
                        break;
                    case WasmMnemonic.Block:
                    case WasmMnemonic.Loop:
                    {
                        var (paramCount, resultCount) = DecodeBlockType((long)insn.Operands[0]);
                        var isLoop = insn.Mnemonic == WasmMnemonic.Loop;
                        var endIp = wasmInstructions[endIndexFor[i]].Ip;

                        _frames.Add(new ControlFrame
                        {
                            IsLoop = isLoop,
                            EntryHeight = _stackHeight - paramCount,
                            ParamCount = paramCount,
                            ResultCount = resultCount,
                            TargetIp = isLoop ? insn.Ip : endIp,
                            EndIp = endIp
                        });

                        if (isLoop)
                        {
                            _constants.Clear(); // the back edge invalidates anything known here
                            Add(OpCode.Nop); // back edge target
                        }

                        break;
                    }
                    case WasmMnemonic.If:
                    {
                        var (paramCount, resultCount) = DecodeBlockType((long)insn.Operands[0]);
                        var condition = Pop();
                        var endIp = wasmInstructions[endIndexFor[i]].Ip;

                        _frames.Add(new ControlFrame
                        {
                            EntryHeight = _stackHeight - paramCount,
                            ParamCount = paramCount,
                            ResultCount = resultCount,
                            TargetIp = endIp,
                            EndIp = endIp
                        });

                        // the false edge lands after the else, or on the end if there isn't one
                        var falseTarget = elseIndexFor.TryGetValue(i, out var elseIndex)
                            ? wasmInstructions[elseIndex].NextIp
                            : endIp;

                        var inverse = Temp();
                        Add(OpCode.CheckEqual, inverse, condition, Imm(0));
                        Add(OpCode.ConditionalJump, Imm((long)falseTarget), inverse);
                        break;
                    }
                    case WasmMnemonic.Else:
                    {
                        // end of the then-branch, jump over the else-branch
                        var frame = _frames[^1];
                        Add(OpCode.Jump, Imm((long)frame.EndIp));
                        _stackHeight = frame.EntryHeight + frame.ParamCount;
                        _constants.Clear();
                        break;
                    }
                    case WasmMnemonic.End:
                    {
                        var frame = _frames[^1];
                        _frames.RemoveAt(_frames.Count - 1);
                        _stackHeight = frame.EntryHeight + frame.ResultCount;
                        _constants.Clear();
                        Add(OpCode.Nop); // branch target
                        break;
                    }
                    case WasmMnemonic.Br:
                    {
                        var frame = FrameAt((int)(ulong)insn.Operands[0]);
                        EmitBranchFixups(frame);
                        Add(OpCode.Jump, Imm((long)frame.TargetIp));
                        _dead = true;
                        break;
                    }
                    case WasmMnemonic.BrIf:
                        EmitConditionalBranch(Pop(), FrameAt((int)(ulong)insn.Operands[0]));
                        break;
                    case WasmMnemonic.BrTable:
                    {
                        var labels = (ulong[])insn.Operands[0];
                        var defaultLabel = (ulong)insn.Operands[1];
                        var index = Pop();

                        for (var caseIndex = 0; caseIndex < labels.Length; caseIndex++)
                        {
                            var matches = Temp();
                            Add(OpCode.CheckEqual, matches, index, Imm(caseIndex));
                            EmitConditionalBranch(matches, FrameAt((int)labels[caseIndex]));
                        }

                        var defaultFrame = FrameAt((int)defaultLabel);
                        EmitBranchFixups(defaultFrame);
                        Add(OpCode.Jump, Imm((long)defaultFrame.TargetIp));
                        _dead = true;
                        break;
                    }
                    case WasmMnemonic.Return:
                        EmitReturn();
                        _dead = true;
                        break;
                    case WasmMnemonic.Call:
                    {
                        var functionIndex = (int)(ulong)insn.Operands[0];
                        var callee = file.FunctionTable[functionIndex];
                        var calleeType = callee.GetType(file);
                        EmitCall(Imm(functionIndex), (int)calleeType.ParamCount, calleeType.ReturnCount > 0, functionIndex);
                        break;
                    }
                    case WasmMnemonic.CallIndirect:
                    {
                        var calleeType = file.GetTypeEntry((int)(ulong)insn.Operands[0]);
                        EmitCall(Pop(), (int)calleeType.ParamCount, calleeType.ReturnCount > 0, null);
                        break;
                    }
                    case WasmMnemonic.Drop:
                        _stackHeight--;
                        break;
                    case WasmMnemonic.Select:
                    {
                        var condition = Pop();
                        var falseValue = Pop();
                        var skip = Add(OpCode.ConditionalJump, Imm(0), condition); // nonzero keeps the first value, already in place
                        Add(OpCode.Move, Stack(_stackHeight - 1), falseValue);
                        skip.SetOperand(0, Add(OpCode.Nop));
                        break;
                    }
                    case WasmMnemonic.LocalGet:
                    {
                        var local = "L" + (ulong)insn.Operands[0];
                        var dest = Push();
                        Add(OpCode.Move, dest, new Register(null, local));
                        if (_constants.TryGetValue(local, out var known))
                            _constants[dest.Name] = known;
                        break;
                    }
                    case WasmMnemonic.LocalSet:
                    case WasmMnemonic.LocalTee:
                    {
                        var local = "L" + (ulong)insn.Operands[0];
                        var source = insn.Mnemonic == WasmMnemonic.LocalSet ? Pop() : Stack(_stackHeight - 1);
                        Add(OpCode.Move, new Register(null, local), source);
                        if (_constants.TryGetValue(source.Name, out var known))
                            _constants[local] = known;
                        break;
                    }
                    case WasmMnemonic.GlobalGet:
                        Add(OpCode.Move, Push(), new Register(null, "G" + (ulong)insn.Operands[0]));
                        break;
                    case WasmMnemonic.GlobalSet:
                        Add(OpCode.Move, new Register(null, "G" + (ulong)insn.Operands[0]), Pop());
                        break;
                    case >= WasmMnemonic.I32Load and <= WasmMnemonic.I64Load32_U:
                    {
                        var address = Pop();
                        Add(OpCode.Move, Push(), MemoryAccess(address, (long)(ulong)insn.Operands[1]));
                        break;
                    }
                    case >= WasmMnemonic.I32Store and <= WasmMnemonic.I64Store32:
                    {
                        var value = Pop();
                        var address = Pop();
                        Add(OpCode.Move, MemoryAccess(address, (long)(ulong)insn.Operands[1]), value);
                        break;
                    }
                    case WasmMnemonic.MemorySize:
                        Add(OpCode.Move, Push(), Temp("WASMMEM"));
                        break;
                    case WasmMnemonic.MemoryGrow:
                        Pop();
                        Add(OpCode.Move, Push(), Temp("WASMMEM"));
                        break;
                    case WasmMnemonic.I32Const:
                    case WasmMnemonic.I64Const:
                    {
                        var value = (long)insn.Operands[0];
                        var dest = Push();
                        Add(OpCode.Move, dest, Imm(value));
                        _constants[dest.Name] = value;
                        break;
                    }
                    case WasmMnemonic.F32Const:
                        Add(OpCode.Move, Push(), new FloatLiteral((float)insn.Operands[0]));
                        break;
                    case WasmMnemonic.F64Const:
                        Add(OpCode.Move, Push(), new DoubleLiteral((double)insn.Operands[0]));
                        break;
                    case WasmMnemonic.I32Eqz:
                    case WasmMnemonic.I64Eqz:
                    {
                        var value = Pop();
                        Add(OpCode.CheckEqual, Push(), value, Imm(0));
                        break;
                    }
                    case >= WasmMnemonic.I32Eq and <= WasmMnemonic.I32Ge_U:
                    case >= WasmMnemonic.I64Eq and <= WasmMnemonic.I64Ge_U:
                    case >= WasmMnemonic.F32Eq and <= WasmMnemonic.F32Ge:
                    case >= WasmMnemonic.F64Eq and <= WasmMnemonic.F64Ge:
                        EmitBinary(ComparisonOpCode(insn.Mnemonic));
                        break;
                    case WasmMnemonic.I32Add:
                    case WasmMnemonic.I64Add:
                    case WasmMnemonic.F32Add:
                    case WasmMnemonic.F64Add:
                        EmitBinary(OpCode.Add);
                        break;
                    case WasmMnemonic.I32Sub:
                    case WasmMnemonic.I64Sub:
                    case WasmMnemonic.F32Sub:
                    case WasmMnemonic.F64Sub:
                        EmitBinary(OpCode.Subtract);
                        break;
                    case WasmMnemonic.I32Mul:
                    case WasmMnemonic.I64Mul:
                    case WasmMnemonic.F32Mul:
                    case WasmMnemonic.F64Mul:
                        EmitBinary(OpCode.Multiply);
                        break;
                    case WasmMnemonic.I32Div_S:
                    case WasmMnemonic.I32Div_U:
                    case WasmMnemonic.I64Div_S:
                    case WasmMnemonic.I64Div_U:
                    case WasmMnemonic.F32Div:
                    case WasmMnemonic.F64Div:
                        EmitBinary(OpCode.Divide);
                        break;
                    case WasmMnemonic.I32Rem_S:
                    case WasmMnemonic.I32Rem_U:
                    case WasmMnemonic.I64Rem_S:
                    case WasmMnemonic.I64Rem_U:
                    {
                        // a - (a / b) * b
                        var b = Pop();
                        var a = Pop();
                        var temp = Temp();
                        Add(OpCode.Divide, temp, a, b);
                        Add(OpCode.Multiply, temp, temp, b);
                        Add(OpCode.Subtract, Push(), a, temp);
                        break;
                    }
                    case WasmMnemonic.I32And:
                    case WasmMnemonic.I64And:
                        EmitBinary(OpCode.And);
                        break;
                    case WasmMnemonic.I32Or:
                    case WasmMnemonic.I64Or:
                        EmitBinary(OpCode.Or);
                        break;
                    case WasmMnemonic.I32Xor:
                    case WasmMnemonic.I64Xor:
                        EmitBinary(OpCode.Xor);
                        break;
                    case WasmMnemonic.I32Shl:
                    case WasmMnemonic.I64Shl:
                        EmitBinary(OpCode.ShiftLeft);
                        break;
                    case WasmMnemonic.I32Shr_S:
                    case WasmMnemonic.I32Shr_U:
                    case WasmMnemonic.I64Shr_S:
                    case WasmMnemonic.I64Shr_U:
                        EmitBinary(OpCode.ShiftRight);
                        break;
                    case WasmMnemonic.I32Rotl:
                    case WasmMnemonic.I64Rotl:
                    case WasmMnemonic.I32Rotr:
                    case WasmMnemonic.I64Rotr:
                    {
                        var width = insn.Mnemonic is WasmMnemonic.I32Rotl or WasmMnemonic.I32Rotr ? 32 : 64;
                        var rotateLeft = insn.Mnemonic is WasmMnemonic.I32Rotl or WasmMnemonic.I64Rotl;
                        var b = Pop();
                        var a = Pop();
                        var high = Temp("TEMP1");
                        var low = Temp("TEMP2");
                        Add(rotateLeft ? OpCode.ShiftLeft : OpCode.ShiftRight, high, a, b);
                        Add(OpCode.Subtract, low, Imm(width), b);
                        Add(rotateLeft ? OpCode.ShiftRight : OpCode.ShiftLeft, low, a, low);
                        Add(OpCode.Or, Push(), high, low);
                        break;
                    }
                    case WasmMnemonic.F32Neg:
                    case WasmMnemonic.F64Neg:
                    {
                        var value = Pop();
                        Add(OpCode.Negate, Push(), value);
                        break;
                    }
                    case WasmMnemonic.I32Clz:
                    case WasmMnemonic.I32Ctz:
                    case WasmMnemonic.I32PopCnt:
                    case WasmMnemonic.I64Clz:
                    case WasmMnemonic.I64Ctz:
                    case WasmMnemonic.I64PopCnt:
                    case WasmMnemonic.F32Trunc:
                    case WasmMnemonic.F32Nearest:
                    case WasmMnemonic.F64Trunc:
                    case WasmMnemonic.F64Nearest:
                        // unary, and the input already sits in the result slot, so dataflow survives
                        Add(OpCode.NotImplemented, new StringLiteral($"Wasm instruction {insn.Mnemonic} not implemented"));
                        break;
                    case WasmMnemonic.F32Abs:
                    case WasmMnemonic.F32Sqrt:
                    case WasmMnemonic.F32Ceil:
                    case WasmMnemonic.F32Floor:
                    case WasmMnemonic.F64Abs:
                    case WasmMnemonic.F64Sqrt:
                    case WasmMnemonic.F64Ceil:
                    case WasmMnemonic.F64Floor:
                        EmitMathIntrinsic(MathMethodName(insn.Mnemonic), insn.Mnemonic is >= WasmMnemonic.F64Abs, 1);
                        break;
                    case WasmMnemonic.F32Min:
                    case WasmMnemonic.F32Max:
                    case WasmMnemonic.F64Min:
                    case WasmMnemonic.F64Max:
                        EmitMathIntrinsic(MathMethodName(insn.Mnemonic), insn.Mnemonic is >= WasmMnemonic.F64Abs, 2);
                        break;
                    case WasmMnemonic.F32Copysign:
                    case WasmMnemonic.F64Copysign:
                        _stackHeight--; // net one pop, first operand's slot doubles as the result
                        Add(OpCode.NotImplemented, new StringLiteral($"Wasm instruction {insn.Mnemonic} not implemented"));
                        break;
                    case >= WasmMnemonic.I32Wrap_I64 and <= WasmMnemonic.F64Reinterpret_I64:
                        // conversions are value-preserving for analysis purposes, and the slot doesn't move
                        break;
                    default:
                        Add(OpCode.NotImplemented, new StringLiteral($"Wasm instruction {insn.Mnemonic} not implemented"));
                        break;
                }
            }

            if (_instructions.Count == 0 || _instructions[^1].OpCode != OpCode.Return)
                EmitReturn();

            foreach (var instruction in _instructions)
            {
                if (instruction.OpCode is not (OpCode.Jump or OpCode.ConditionalJump))
                    continue;

                if (instruction.Operands[0] is not Immediate target)
                    continue; // already resolved to an instruction directly

                if (_ipToIndex.TryGetValue(target.UnsignedValue, out var targetIndex) && targetIndex < _instructions.Count)
                {
                    instruction.SetOperand(0, _instructions[targetIndex]);
                }
                else
                {
                    instruction.OpCode = OpCode.Invalid;
                    instruction.SetOperands(new StringLiteral($"Jump target not found in method: 0x{target.UnsignedValue:X4}"));
                }
            }

            return _instructions;
        }

        private static OpCode ComparisonOpCode(WasmMnemonic mnemonic) => mnemonic switch
        {
            WasmMnemonic.I32Eq or WasmMnemonic.I64Eq or WasmMnemonic.F32Eq or WasmMnemonic.F64Eq => OpCode.CheckEqual,
            WasmMnemonic.I32Ne or WasmMnemonic.I64Ne or WasmMnemonic.F32Ne or WasmMnemonic.F64Ne => OpCode.CheckNotEqual,
            WasmMnemonic.I32Lt_S or WasmMnemonic.I32Lt_U or WasmMnemonic.I64Lt_S or WasmMnemonic.I64Lt_U or WasmMnemonic.F32Lt or WasmMnemonic.F64Lt => OpCode.CheckLess,
            WasmMnemonic.I32Gt_S or WasmMnemonic.I32Gt_U or WasmMnemonic.I64Gt_S or WasmMnemonic.I64Gt_U or WasmMnemonic.F32Gt or WasmMnemonic.F64Gt => OpCode.CheckGreater,
            WasmMnemonic.I32Le_S or WasmMnemonic.I32Le_U or WasmMnemonic.I64Le_S or WasmMnemonic.I64Le_U or WasmMnemonic.F32Le or WasmMnemonic.F64Le => OpCode.CheckLessOrEqual,
            _ => OpCode.CheckGreaterOrEqual
        };

        private IOperand MemoryAccess(Register address, long offset) =>
            _constants.TryGetValue(address.Name, out var known)
                ? new MemoryOperand(addend: known + offset)
                : new MemoryOperand(address, addend: offset);

        private static string MathMethodName(WasmMnemonic mnemonic) => mnemonic switch
        {
            WasmMnemonic.F32Abs or WasmMnemonic.F64Abs => "Abs",
            WasmMnemonic.F32Sqrt or WasmMnemonic.F64Sqrt => "Sqrt",
            WasmMnemonic.F32Ceil or WasmMnemonic.F64Ceil => "Ceiling",
            WasmMnemonic.F32Floor or WasmMnemonic.F64Floor => "Floor",
            WasmMnemonic.F32Min or WasmMnemonic.F64Min => "Min",
            _ => "Max"
        };

        // maps a float intrinsic to the matching System.Math call, falling back to a marker if it can't resolve
        private void EmitMathIntrinsic(string name, bool isDouble, int argumentCount)
        {
            var arguments = new List<IOperand>(argumentCount);
            for (var a = 0; a < argumentCount; a++)
                arguments.Add(Stack(_stackHeight - argumentCount + a));

            _stackHeight -= argumentCount;

            var method = resolveMathMethod(context.AppContext, name, isDouble, argumentCount);

            if (method == null)
            {
                _stackHeight += argumentCount; // leave the operands in place, dataflow survives
                Add(OpCode.NotImplemented, new StringLiteral($"System.Math.{name} not resolved"));
                return;
            }

            var call = Add(OpCode.Call, method, Push());
            call.AddOperands(arguments);
        }

        private void EmitBinary(OpCode opCode)
        {
            var b = Pop();
            var a = Pop();
            var hasConstants = _constants.TryGetValue(a.Name, out var aValue) & _constants.TryGetValue(b.Name, out var bValue);
            var dest = Push();
            Add(opCode, dest, a, b);

            // fold address arithmetic so loads relative to a constant base stay constant
            if (hasConstants && opCode is OpCode.Add or OpCode.Subtract)
                _constants[dest.Name] = opCode == OpCode.Add ? aValue + bValue : aValue - bValue;
        }

        private void EmitCall(IOperand target, int argumentCount, bool returnsOnStack, int? directFunctionIndex)
        {
            var arguments = new List<IOperand>(argumentCount);
            for (var a = 0; a < argumentCount; a++)
                arguments.Add(Stack(_stackHeight - argumentCount + a));

            _stackHeight -= argumentCount;

            // a known managed target lets us drop the hidden return buffer arg to match the shape analysis expects
            MethodAnalysisContext? managed = null;
            if (directFunctionIndex is { } functionIndex
                && context.AppContext.MethodsByAddress.TryGetValue((ulong)functionIndex, out var candidates)
                && candidates.Count > 0)
            {
                managed = candidates[0];
                foreach (var candidate in candidates)
                {
                    if (candidate.Parameters.Count > managed.Parameters.Count)
                        managed = candidate;
                }
            }

            if (managed != null && WasmUtils.HasReturnBuffer(managed) && arguments.Count > 0)
                arguments.RemoveAt(0);

            Instruction call;

            if (returnsOnStack)
                call = directFunctionIndex != null
                    ? Add(OpCode.Call, target, Push())
                    : Add(OpCode.IndirectCall, target, Push());
            else if (directFunctionIndex == null)
                call = Add(OpCode.IndirectCall, target, Temp("SRET")); // operand 1 must exist to be the (possibly unused) destination
            else if (managed is { IsVoid: false })
                call = Add(OpCode.Call, target, Temp("SRET")); // hidden buffer return, the wasm function itself is void
            else
                call = Add(OpCode.CallVoid, target);

            call.AddOperands(arguments);
        }
    }
}
