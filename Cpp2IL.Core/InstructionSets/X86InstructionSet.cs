using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.InstructionSets;

// This is honestly an X64InstructionSet by all means. Everything here screams "I AM X64".
public class X86InstructionSet : Cpp2IlInstructionSet
{
    private static readonly MasmFormatter Formatter = new();
    private static readonly StringOutput Output = new();
    private static readonly X64CallingConventionResolver CallingConventions = new();

    public override BaseCallingConventionResolver CallingConventionResolver => CallingConventions;

    private static ISIL.Immediate Imm(long value) => new(value);
    private static ISIL.Immediate Imm(ulong value) => new(unchecked((long)value));

    private static string FormatInstructionInternal(Instruction instruction)
    {
        Formatter.Format(instruction, Output);
        return Output.ToStringAndReset();
    }

    public static string FormatInstruction(Instruction instruction)
    {
        lock (Formatter)
        {
            return FormatInstructionInternal(instruction);
        }
    }

    public override BinarySlice GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator, context.AppContext.Binary);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        lock (Formatter)
        {
            var insns = X86Utils.Iterate(context);

            return string.Join("\n", insns.Select(FormatInstructionInternal));
        }
    }

    public override List<ISIL.Instruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var instructions = new List<ISIL.Instruction>();
        var addresses = new List<ulong>();

        foreach (var instruction in X86Utils.Iterate(context))
            ConvertInstructionStatement(instruction, instructions, addresses, context);

        // Add return if the function doesn't end with one already
        if (instructions.Count > 0 && instructions[^1].OpCode != ISIL.OpCode.Return)
        {
            var index = instructions[^1].Index + 1;

            if (context.IsVoid)
                instructions.Add(new ISIL.Instruction(index, ISIL.OpCode.Return));
            else if (context.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                instructions.Add(new ISIL.Instruction(index, ISIL.OpCode.Return, new ISIL.Register(null, "xmm0")));
            else
                instructions.Add(new ISIL.Instruction(index, ISIL.OpCode.Return, new ISIL.Register(null, "rax")));
        }

        // fix branches
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            if (instruction.OpCode != ISIL.OpCode.Jump && instruction.OpCode != ISIL.OpCode.ConditionalJump)
                continue;

            var targetAddress = ((ISIL.Immediate)instruction.Operands[0]).UnsignedValue;
            var targetIndex = addresses.FindIndex(addr => addr == targetAddress);

            if (targetIndex == -1)
            {
                instruction.OpCode = ISIL.OpCode.Invalid;
                instruction.SetOperands(new ISIL.StringLiteral($"Jump target not found in method: 0x{targetAddress:X4}"));
                continue;
            }

            var targetInstruction = instructions[targetIndex];

            instruction.SetOperand(0, targetInstruction);
        }

        return instructions;
    }

    private static ISIL.Register? ReturnRegisterClobberedBy(MethodAnalysisContext callee)
        => callee.IsVoid ? CallingConventions.ReturnRegister(callee) : null;

    public override List<ISIL.IOperand> GetParameterOperandsFromMethod(MethodAnalysisContext context)
    {
        return CallingConventions.ResolveForManaged(context).ToList();
    }

    public override ulong GetThunkTarget(ApplicationAnalysisContext context, ulong thunkAddress)
    {
        var binary = context.Binary;

        if (!binary.TryMapVirtualAddressToRaw(thunkAddress, out var rawAddress))
            return 0;

        var raw = binary.GetRawBinaryContent();
        var length = (int)Math.Min(32, raw.Length - rawAddress);
        if (length <= 0)
            return 0;

        var decoder = Decoder.Create(binary.is32Bit ? 32 : 64, new ByteArrayCodeReader(raw.Slice((int)rawAddress, length).ToArray()), thunkAddress);
        
        for (var i = 0; i < 4; i++)
        {
            var instruction = decoder.Decode();

            if (instruction.FlowControl == FlowControl.UnconditionalBranch && instruction.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64)
                return instruction.NearBranchTarget;

            if (instruction.FlowControl != FlowControl.Next)
                return 0;
        }

        return 0;
    }

    public override ulong GetInternalCallTarget(MethodAnalysisContext method)
    {
        var start = GetPointerForMethod(method);
        var length = method.RawBytes.Length;

        if (start == 0 || length == 0)
            return 0;

        var decoder = Decoder.Create(method.AppContext.Binary.is32Bit ? 32 : 64, new ByteArrayCodeReader(method.RawBytes.ToArray()), start);
        var target = 0ul;

        while (decoder.IP < start + (ulong)length)
        {
            var instruction = decoder.Decode();

            if (instruction.FlowControl != FlowControl.UnconditionalBranch || instruction.Op0Kind is not (OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64))
                continue;

            var branch = instruction.NearBranchTarget;

            if (branch >= start && branch < start + (ulong)length)
                continue; // ordinary control flow within the stub

            if (target != 0 && target != branch)
                return 0;

            target = branch;
        }

        return target;
    }

    public override (IReadOnlyList<ulong> DataReferences, IReadOnlyList<ulong> CallTargets) InspectPotentialThrowHelper(ApplicationAnalysisContext context, ulong address)
    {
        Iced.Intel.InstructionList body;
        try
        {
            body = X86Utils.GetMethodBodyAtVirtAddressNew(address, true, context.Binary);
        }
        catch
        {
            return ([], []);
        }

        var dataReferences = new List<ulong>();
        var callTargets = new List<ulong>();

        foreach (var insn in body)
        {
            if (insn.Mnemonic == Mnemonic.Lea && insn.IsIPRelativeMemoryOperand)
                dataReferences.Add(insn.IPRelativeMemoryAddress);
            else if (insn.Mnemonic == Mnemonic.Call && insn.Op0Kind == OpKind.NearBranch64)
                callTargets.Add(insn.NearBranchTarget);
        }

        return (dataReferences, callTargets);
    }

    internal List<ISIL.Instruction> GetIsilFromInstruction(Instruction instruction)
    {
        var instructions = new List<ISIL.Instruction>();
        ConvertInstructionStatement(instruction, instructions, [], null!);
        return instructions;
    }

    private void ConvertInstructionStatement(Instruction instruction, List<ISIL.Instruction> instructions, List<ulong> addresses, MethodAnalysisContext context)
    {
        var callNoReturn = false;
        int operandSize;

        ISIL.Instruction Add(ulong address, ISIL.OpCode opCode, params List<ISIL.IOperand> operands)
        {
            addresses.Add(address);
            var newInstruction = new ISIL.Instruction(instructions.Count, opCode, operands);
            instructions.Add(newInstruction);
            return newInstruction;
        }

        // Preserve all the argument registers as we don't know which ones are used
        void AddIndirectCall(Instruction source)
        {
            var call = Add(source.IP, ISIL.OpCode.IndirectCall, ConvertOperand(source, 0), new ISIL.Register(null, "rax") /* return value */);
            call.AddOperands(CallingConventions.ResolveForUnmanaged(context.AppContext, source.IP));
        }
        
        // Preserve all the argument registers as we don't know which ones are used
        void AddIndirectJmp(Instruction source)
        {
            var call = Add(source.IP, ISIL.OpCode.IndirectJump, ConvertOperand(source, 0), new ISIL.Register(null, "rax") /* return value */);
            call.AddOperands(CallingConventions.ResolveForUnmanaged(context.AppContext, source.IP));
        }

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov:
            case Mnemonic.Movzx: // For all intents and purposes we don't care about zero-extending
            case Mnemonic.Movsx: // move with sign-extendign
            case Mnemonic.Movsxd: // same
            case Mnemonic.Movaps: // Movaps is basically just a mov but with the potential future detail that the size is dependent on reg size
            case Mnemonic.Movups: // Movaps but unaligned
            case Mnemonic.Movd: // Mov but specifically dword
            case Mnemonic.Movq: // Mov but specifically qword
            case Mnemonic.Movdqa: // Movaps but multiple integers at once in theory
            case Mnemonic.Cvtdq2ps: // Technically a convert double to single, but for analysis purposes we can just treat it as a move
            case Mnemonic.Cvtps2pd: // same, but float to double
            case Mnemonic.Cvtdq2pd: // int to double
            case Mnemonic.Cvtpd2ps: // double to float
            case Mnemonic.Cvttsd2si: // same, but double to integer
            case Mnemonic.Movdqu: // DEST[127:0] := SRC[127:0]
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Movss: // scalar single - as a move, but a load from a constant address is a float literal
            case Mnemonic.Movsd: // scalar double
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertScalarFloatOperand(instruction, 1, instruction.Mnemonic == Mnemonic.Movss, context));
                break;
            case Mnemonic.Cbw: // AX := sign-extend AL
                Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.AX)),
                    new ISIL.Register(null, X86Utils.GetRegisterName(Register.AL)));
                break;
            case Mnemonic.Cwde: // EAX := sign-extend AX
                Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.EAX)),
                    new ISIL.Register(null, X86Utils.GetRegisterName(Register.AX)));
                break;
            case Mnemonic.Cdqe: // RAX := sign-extend EAX
                Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.RAX)),
                    new ISIL.Register(null, X86Utils.GetRegisterName(Register.EAX)));
                break;
            case Mnemonic.Cwd: // DX:AX := sign-extend AX
                Add(instruction.IP, ISIL.OpCode.ShiftRight, new ISIL.Register(null, X86Utils.GetRegisterName(Register.DX)),
                    new ISIL.Register(null, X86Utils.GetRegisterName(Register.AX)), Imm(15));
                break;
            case Mnemonic.Cdq: // EDX:EAX := sign-extend EAX
                Add(instruction.IP, ISIL.OpCode.ShiftRight, new ISIL.Register(null, X86Utils.GetRegisterName(Register.EDX)),
                    new ISIL.Register(null, X86Utils.GetRegisterName(Register.EAX)), Imm(31));
                break;
            case Mnemonic.Cqo: // RDX:RAX := sign-extend RAX
                Add(instruction.IP, ISIL.OpCode.ShiftRight, new ISIL.Register(null, X86Utils.GetRegisterName(Register.RDX)),
                    new ISIL.Register(null, X86Utils.GetRegisterName(Register.RAX)), Imm(63));
                break;
            case Mnemonic.Lea:
                var destination = ConvertOperand(instruction, 0);

                // RIP-relative LEA is effectively loading the absolute address.
                if (instruction.IsIPRelativeMemoryOperand)
                {
                    Add(instruction.IP, ISIL.OpCode.Move, destination, Imm((long)instruction.IPRelativeMemoryAddress));
                    return;
                }

                // Stack-address LEA keeps stack semantics represented as address-of stack slot.
                if (instruction is { MemoryBase: Register.RSP, MemoryIndex: Register.None })
                {
                    Add(instruction.IP, ISIL.OpCode.Move, destination, ConvertOperand(instruction, 1, true));
                    return;
                }

                // Absolute-address LEA also computes a value rather than loading from memory.
                if (instruction.MemoryBase == Register.None && instruction.MemoryIndex == Register.None)
                {
                    Add(instruction.IP, ISIL.OpCode.Move, destination, Imm((long)instruction.MemoryDisplacement64));
                    return;
                }

                if (instruction.MemoryIndex != Register.None)
                {
                    ISIL.IOperand? baseRegister = instruction.MemoryBase != Register.None
                        ? new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryBase))
                        : null;
                    var indexRegister = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryIndex));
                    var source = (ISIL.IOperand)indexRegister;

                    if (instruction.MemoryIndexScale > 1)
                    {
                        if (baseRegister != null)
                        {
                            var temp = new ISIL.Register(null, "TEMP");
                            Add(instruction.IP, ISIL.OpCode.Multiply, temp, indexRegister, Imm(instruction.MemoryIndexScale));
                            source = temp;
                        }
                        else
                        {
                            Add(instruction.IP, ISIL.OpCode.Multiply, destination, indexRegister, Imm(instruction.MemoryIndexScale));
                            source = destination;
                        }
                    }

                    if (baseRegister != null)
                        Add(instruction.IP, ISIL.OpCode.Add, destination, baseRegister, source);
                    else if (!ReferenceEquals(source, destination))
                        Add(instruction.IP, ISIL.OpCode.Move, destination, source);

                    var displacement = unchecked((long)instruction.MemoryDisplacement64);
                    if (displacement > 0)
                        Add(instruction.IP, ISIL.OpCode.Add, destination, destination, Imm(displacement));
                    else if (displacement < 0)
                        Add(instruction.IP, ISIL.OpCode.Subtract, destination, destination, Imm(-displacement));

                    return;
                }

                if (instruction.MemoryBase != Register.None && instruction.MemoryBase != Register.RSP)
                {
                    var baseRegister = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryBase));
                    var displacement = unchecked((long)instruction.MemoryDisplacement64);

                    if (displacement == 0)
                        Add(instruction.IP, ISIL.OpCode.Move, destination, baseRegister);
                    else if (displacement > 0)
                        Add(instruction.IP, ISIL.OpCode.Add, destination, baseRegister, Imm(displacement));
                    else
                        Add(instruction.IP, ISIL.OpCode.Subtract, destination, baseRegister, Imm(-displacement));

                    return;
                }

                Add(instruction.IP, ISIL.OpCode.Move, destination, ConvertOperand(instruction, 1, true));
                break;
            case Mnemonic.Xor:
            case Mnemonic.Xorps: //xorps is just floating point xor
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                    Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), Imm(0));
                else
                    Add(instruction.IP, ISIL.OpCode.Xor, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shl: // unsigned shift
            case Mnemonic.Sal: // signed shift
                Add(instruction.IP, ISIL.OpCode.ShiftLeft, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shr: // unsigned shift
            case Mnemonic.Sar: // signed shift
                Add(instruction.IP, ISIL.OpCode.ShiftRight, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.And:
            case Mnemonic.Andps: //Floating point and
                Add(instruction.IP, ISIL.OpCode.And, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Or:
            case Mnemonic.Orps: //Floating point or
                Add(instruction.IP, ISIL.OpCode.Or, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Bts: // CF = old bit, then set it
                {
                    var dest = ConvertOperand(instruction, 0);
                    var bit = ConvertOperand(instruction, 1);
                    var temp = new ISIL.Register(null, "TEMP");
                    Add(instruction.IP, ISIL.OpCode.ShiftRight, temp, dest, bit);
                    Add(instruction.IP, ISIL.OpCode.And, new ISIL.Register(null, "CF"), temp, Imm(1));
                    Add(instruction.IP, ISIL.OpCode.ShiftLeft, temp, Imm(1), bit);
                    Add(instruction.IP, ISIL.OpCode.Or, dest, dest, temp);
                    break;
                }
            case Mnemonic.Btr: // CF = old bit, then clear it
                {
                    var dest = ConvertOperand(instruction, 0);
                    var bit = ConvertOperand(instruction, 1);
                    var temp = new ISIL.Register(null, "TEMP");
                    Add(instruction.IP, ISIL.OpCode.ShiftRight, temp, dest, bit);
                    Add(instruction.IP, ISIL.OpCode.And, new ISIL.Register(null, "CF"), temp, Imm(1));
                    Add(instruction.IP, ISIL.OpCode.ShiftLeft, temp, Imm(1), bit);
                    Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // temp = ~(1 << bit)
                    Add(instruction.IP, ISIL.OpCode.And, dest, dest, temp);
                    break;
                }
            case Mnemonic.Not:
                Add(instruction.IP, ISIL.OpCode.Not, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Neg: // dest := -dest
                Add(instruction.IP, ISIL.OpCode.Negate, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Imul:
                if (instruction.OpCount == 1)
                {
                    int opSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                    switch (opSize) // TODO: I don't know how to work with dual registers here, I left hints though
                    {
                        case 1: // Op0 * AL -> AX
                            Add(instruction.IP, ISIL.OpCode.Multiply, Register.AX.MakeIndependent(), ConvertOperand(instruction, 0), Register.AL.MakeIndependent());
                            return;
                        case 2: // Op0 * AX -> DX:AX

                            break;
                        case 4: // Op0 * EAX -> EDX:EAX

                            break;
                        case 8: // Op0 * RAX -> RDX:RAX

                            break;
                        default: // prob 0, I think fallback to architecture alignment would be good here(issue: idk how to find out arch alignment)

                            break;
                    }

                    // if got to here, it didn't work
                    goto default;
                }
                else if (instruction.OpCount == 3) Add(instruction.IP, ISIL.OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else Add(instruction.IP, ISIL.OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));

                break;
            case Mnemonic.Idiv:
            case Mnemonic.Div:
                {
                    var divisorSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();

                    // the 8-bit form puts the remainder in AH, which we can't deal with
                    if (divisorSize is not (2 or 4 or 8))
                        goto default;

                    // The real dividend is the D:A register pair, but every compiler sets D up with cdq/cqo or
                    // an xor immediately beforehand, so in reality it's just rax
                    var quotient = new ISIL.Register(null, X86Utils.GetRegisterName(Register.RAX));
                    var remainder = new ISIL.Register(null, X86Utils.GetRegisterName(Register.RDX));

                    var dividend = new ISIL.Register(null, "TEMP_DIVIDEND");
                    var divisor = new ISIL.Register(null, "TEMP_DIVISOR");

                    Add(instruction.IP, ISIL.OpCode.Move, dividend, quotient);
                    Add(instruction.IP, ISIL.OpCode.Move, divisor, ConvertOperand(instruction, 0));
                    Add(instruction.IP, ISIL.OpCode.Divide, quotient, dividend, divisor);
                    Add(instruction.IP, ISIL.OpCode.Modulo, remainder, dividend, divisor);
                    break;
                }
            case Mnemonic.Mulss:
            case Mnemonic.Vmulss:
                if (instruction.OpCount == 3)
                    Add(instruction.IP, ISIL.OpCode.Multiply, ConvertOperand(instruction, 0), ConvertScalarFloatOperand(instruction, 1, true, context), ConvertScalarFloatOperand(instruction, 2, true, context));
                else if (instruction.OpCount == 2)
                    Add(instruction.IP, ISIL.OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertScalarFloatOperand(instruction, 1, true, context));
                else
                    goto default;

                break;

            case Mnemonic.Divss: // Divide Scalar Single Precision Floating-Point Values. DEST[31:0] = DEST[31:0] / SRC[31:0]
                Add(instruction.IP, ISIL.OpCode.Divide, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertScalarFloatOperand(instruction, 1, true, context));
                break;
            case Mnemonic.Vdivss: // VEX Divide Scalar Single Precision Floating-Point Values. DEST[31:0] = SRC1[31:0] / SRC2[31:0]
                Add(instruction.IP, ISIL.OpCode.Divide, ConvertOperand(instruction, 0), ConvertScalarFloatOperand(instruction, 1, true, context), ConvertScalarFloatOperand(instruction, 2, true, context));
                break;

            case Mnemonic.Ret:
                // TODO: Verify correctness of operation with Vectors.

                // On x32, this will require better engineering since ulongs are handled somehow differently (return in 2 registers, I think?)
                // The x64 prototype should work.
                // Are st* registers even used in il2cpp games?

                if (context.IsVoid)
                    Add(instruction.IP, ISIL.OpCode.Return);
                else if (context.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                    Add(instruction.IP, ISIL.OpCode.Return, new ISIL.Register(null, "xmm0"));
                else
                    Add(instruction.IP, ISIL.OpCode.Return, new ISIL.Register(null, "rax"));
                break;
            case Mnemonic.Push:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                Add(instruction.IP, ISIL.OpCode.ShiftStack, Imm(-operandSize));
                Add(instruction.IP, ISIL.OpCode.Move, new ISIL.StackOffset(0), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Pop:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), new ISIL.StackOffset(0));
                Add(instruction.IP, ISIL.OpCode.ShiftStack, Imm(operandSize));
                break;
            case Mnemonic.Sub:
            case Mnemonic.Add:
                var isSubtract = instruction.Mnemonic == Mnemonic.Sub;

                // Special case - stack shift
                if (instruction.Op0Register == Register.RSP && instruction.Op1Kind.IsImmediate())
                {
                    var amount = (int)instruction.GetImmediate(1);
                    Add(instruction.IP, ISIL.OpCode.ShiftStack, Imm(isSubtract ? -amount : amount));
                    break;
                }

                var left = ConvertOperand(instruction, 0);
                var right = ConvertOperand(instruction, 1);
                if (isSubtract)
                    Add(instruction.IP, ISIL.OpCode.Subtract, left, left, right);
                else
                    Add(instruction.IP, ISIL.OpCode.Add, left, left, right);

                break;
            case Mnemonic.Addss:
            case Mnemonic.Subss:
                {
                    // Addss and subss are just floating point add/sub, but we don't need to handle the stack stuff
                    // But we do need to handle 2 vs 3 operand forms
                    ISIL.IOperand dest;
                    ISIL.IOperand src1;
                    ISIL.IOperand src2;

                    if (instruction.OpCount == 3)
                    {
                        //dest, src1, src2
                        dest = ConvertOperand(instruction, 0);
                        src1 = ConvertScalarFloatOperand(instruction, 1, true, context);
                        src2 = ConvertScalarFloatOperand(instruction, 2, true, context);
                    }
                    else if (instruction.OpCount == 2)
                    {
                        //DestAndSrc1, Src2
                        dest = ConvertOperand(instruction, 0);
                        src1 = dest;
                        src2 = ConvertScalarFloatOperand(instruction, 1, true, context);
                    }
                    else
                        goto default;

                    if (instruction.Mnemonic == Mnemonic.Subss)
                        Add(instruction.IP, ISIL.OpCode.Subtract, dest, src1, src2);
                    else
                        Add(instruction.IP, ISIL.OpCode.Add, dest, src1, src2);
                    break;
                }
            // The following pair of instructions does not update the Carry Flag (CF):
            case Mnemonic.Dec:
                Add(instruction.IP, ISIL.OpCode.Subtract, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), Imm(1));
                break;
            case Mnemonic.Inc:
                Add(instruction.IP, ISIL.OpCode.Add, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), Imm(1));
                break;

            case Mnemonic.Shufps: // Packed Interleave Shuffle of Quadruplets of Single Precision Floating-Point Values
                {
                    if (instruction.Op1Kind == OpKind.Memory)
                        goto default;

                    var imm = instruction.Immediate8;
                    var src1 = X86Utils.GetRegisterName(instruction.Op0Register);
                    var src2 = X86Utils.GetRegisterName(instruction.Op1Register);

                    // Element selection
                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, "XMM_TEMP" + "_0"),
                        new ISIL.Register(null, $"{src1}_{imm & 0b11}"));

                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, "XMM_TEMP" + "_1"),
                        new ISIL.Register(null, $"{src1}_{(imm >> 2) & 0b11}"));

                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, "XMM_TEMP" + "_2"),
                        new ISIL.Register(null, $"{src2}_{(imm >> 4) & 0b11}"));

                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, "XMM_TEMP" + "_3"),
                        new ISIL.Register(null, $"{src2}_{(imm >> 6) & 0b11}"));

                    Add(instruction.IP, ISIL.OpCode.Move,
                        ConvertOperand(instruction, 0),
                        new ISIL.Register(null, "XMM_TEMP"));

                    break;
                }

            case Mnemonic.Unpcklps: // Unpack and Interleave Low Packed Single Precision Floating-Point Values
                {
                    if (instruction.Op1Kind == OpKind.Memory)
                        goto default;

                    var src1 = X86Utils.GetRegisterName(instruction.Op0Register);
                    var src2 = X86Utils.GetRegisterName(instruction.Op1Register);

                    // Interleaving lanes
                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, (string?)"XMM_TEMP" + "_0"),
                        new ISIL.Register(null, $"{src1}_0")); // SRC1[31:0]

                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, (string?)"XMM_TEMP" + "_1"),
                        new ISIL.Register(null, $"{src2}_0")); // SRC2[31:0]

                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, (string?)"XMM_TEMP" + "_2"),
                        new ISIL.Register(null, $"{src1}_1")); // SRC1[63:32]

                    Add(instruction.IP, ISIL.OpCode.Move,
                        new ISIL.Register(null, (string?)"XMM_TEMP" + "_3"),
                        new ISIL.Register(null, $"{src2}_1")); // SRC2[63:32]

                    Add(instruction.IP, ISIL.OpCode.Move,
                        ConvertOperand(instruction, 0),
                        new ISIL.Register(null, (string?)"XMM_TEMP"));

                    break;
                }

            case Mnemonic.Call:
                // We don't try and resolve which method is being called, but we do need to know how many parameters it has
                // I would hope that all of these methods have the same number of arguments, else how can they be inlined?

                var target = instruction.NearBranchTarget;

                if (instruction.Op0Kind == OpKind.Register || instruction.Op0Kind == OpKind.Memory)
                {
                    AddIndirectCall(instruction);
                }
                else if (context.AppContext.MethodsByAddress.TryGetValue(target, out var possibleMethods))
                {
                    if (possibleMethods.Count == 1)
                    {
                        ISIL.Instruction call;

                        if (possibleMethods[0].IsVoid)
                            call = Add(instruction.IP, ISIL.OpCode.CallVoid, Imm(target));
                        else
                            call = Add(instruction.IP, ISIL.OpCode.Call, Imm(target), CallingConventions.ReturnRegister(possibleMethods[0]));

                        call.AddOperands(CallingConventions.ResolveForManaged(possibleMethods[0]));
                        call.ImplicitDefinition = ReturnRegisterClobberedBy(possibleMethods[0]);
                    }
                    else
                    {
                        MethodAnalysisContext ctx = null!;
                        var lpars = -1;

                        // Very naive approach, folds with structs in parameters if GCC is used:
                        foreach (var method in possibleMethods)
                        {
                            var pars = method.Parameters.Count;
                            if (method.IsStatic) pars++;
                            if (pars > lpars)
                            {
                                lpars = pars;
                                ctx = method;
                            }
                        }

                        // On post-analysis, you can discard methods according to the registers used, see CallingConventions.
                        // This is less effective on GCC because MSVC doesn't overlap registers.

                        ISIL.Instruction call;

                        if (ctx.IsVoid)
                            call = Add(instruction.IP, ISIL.OpCode.CallVoid, Imm(target));
                        else
                            call = Add(instruction.IP, ISIL.OpCode.Call, Imm(target), CallingConventions.ReturnRegister(ctx));

                        call.AddOperands(CallingConventions.ResolveForManaged(ctx));
                        call.ImplicitDefinition = ReturnRegisterClobberedBy(ctx);
                    }
                }
                else
                {
                    // This isn't a managed method, so for now we don't know its parameter count.
                    // This will need to be rewritten if we ever stumble upon an unmanaged method that accepts more than 4 parameters.
                    // These can be converted to dedicated ISIL instructions for specific API functions at a later stage. (by a post-processing step)

                    var call = Add(instruction.IP, ISIL.OpCode.Call, Imm(target), new ISIL.Register(null, "rax") /* return value */);
                    call.AddOperands(CallingConventions.ResolveForUnmanaged(context.AppContext, target));
                }

                if (callNoReturn)
                {
                    // Our function decided to jump into a thunk or do a funny return.
                    // We will insert a return after the call.
                    // According to common sense, such callee must have the same return value as the caller, unless it's __noreturn.
                    // I hope someone else will catch up on this and figure out non-returning functions.

                    // TODO: Determine whether a function is an actual thunk and it's *technically better* to duplicate code for it, or if it's a regular retcall.
                    // Basic implementation may use context.AppContext.MethodsByAddress, but this doesn't catch thunks only.
                    // For example, SWDT often calls gc::GarbageCollector::SetWriteBarrier through a long jmp chain. That's a whole function, not just a thunk.

                    goto case Mnemonic.Ret;
                }

                break;
            case Mnemonic.Test:
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                {
                    AddCompareInstruction(instruction.IP, ConvertOperand(instruction, 0), Imm(0));
                    break;
                }
                AddTestInstruction(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Cmp:
                AddCompareInstruction(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Comiss: //comiss is just a floating point compare dest[31:0] == src[31:0]
            case Mnemonic.Ucomiss: // same, but unsigned
                AddCompareInstruction(instruction.IP, ConvertOperand(instruction, 0), ConvertScalarFloatOperand(instruction, 1, true, context));
                break;

            case Mnemonic.Cmove: // move if condition
            case Mnemonic.Cmovne:
            case Mnemonic.Cmova:
            case Mnemonic.Cmovg:
            case Mnemonic.Cmovae:
            case Mnemonic.Cmovge:
            case Mnemonic.Cmovb:
            case Mnemonic.Cmovl:
            case Mnemonic.Cmovbe:
            case Mnemonic.Cmovle:
            case Mnemonic.Cmovs:
            case Mnemonic.Cmovns:
                switch (instruction.Mnemonic)
                {
                    case Mnemonic.Cmove: // equals
                        Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "ZF")); // TEMP = !ZF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), new ISIL.Register(null, "TEMP")); // skip if not eq
                        break;
                    case Mnemonic.Cmovne: // not equals
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), new ISIL.Register(null, "ZF")); // skip if eq
                        break;
                    case Mnemonic.Cmovs: // sign
                        Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "SF")); // TEMP = !SF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), new ISIL.Register(null, "TEMP")); // skip if not sign
                        break;
                    case Mnemonic.Cmovns: // not sign
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), new ISIL.Register(null, "SF")); // skip if sign
                        break;
                    case Mnemonic.Cmova:
                    case Mnemonic.Cmovg: // greater
                        var temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                        Add(instruction.IP, ISIL.OpCode.Or, temp, temp, new ISIL.Register(null, "ZF")); // TEMP = TEMP || ZF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), temp); // skip if not gt
                        break;
                    case Mnemonic.Cmovae:
                    case Mnemonic.Cmovge: // greater or eq
                        temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), temp); // skip if not gt or eq
                        break;
                    case Mnemonic.Cmovb:
                    case Mnemonic.Cmovl: // less
                        temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), temp); // skip if not lt
                        break;
                    case Mnemonic.Cmovbe:
                    case Mnemonic.Cmovle: // less or eq
                        temp = new ISIL.Register(null, "TEMP");
                        var temp2 = new ISIL.Register(null, "TEMP2");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp2, new ISIL.Register(null, "ZF")); // TEMP2 = !ZF
                        Add(instruction.IP, ISIL.OpCode.And, temp, temp, temp2); // TEMP = TEMP && TEMP2
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), temp); // skip if not lt or eq
                        break;
                }
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)); // set if cond
                Add(instruction.IP + 1, ISIL.OpCode.Nop);
                break;

            // Convert a flag condition into the (byte) destination as 0/1, mirroring the Cmov conditions.
            case Mnemonic.Sete: // ZF
            case Mnemonic.Setne: // !ZF
            case Mnemonic.Seta: // above: !CF && !ZF
            case Mnemonic.Setae: // above or equal: !CF
            case Mnemonic.Setb: // below: CF
            case Mnemonic.Setbe: // below or equal: CF || ZF
            case Mnemonic.Setg: // greater: !ZF && SF == OF
            case Mnemonic.Setge: // greater or equal: SF == OF
            case Mnemonic.Setl: // less: SF != OF
            case Mnemonic.Setle: // less or equal: ZF || SF != OF
            case Mnemonic.Sets: // SF
            case Mnemonic.Setns: // !SF
                {
                    var dest = ConvertOperand(instruction, 0);
                    var cf = new ISIL.Register(null, "CF");
                    var zf = new ISIL.Register(null, "ZF");
                    var sf = new ISIL.Register(null, "SF");
                    var of = new ISIL.Register(null, "OF");
                    var temp = new ISIL.Register(null, "TEMP");

                    switch (instruction.Mnemonic)
                    {
                        case Mnemonic.Sete:
                            Add(instruction.IP, ISIL.OpCode.Move, dest, zf);
                            break;
                        case Mnemonic.Setne:
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, dest, zf, Imm(0));
                            break;
                        case Mnemonic.Setb:
                            Add(instruction.IP, ISIL.OpCode.Move, dest, cf);
                            break;
                        case Mnemonic.Setae:
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, dest, cf, Imm(0));
                            break;
                        case Mnemonic.Seta:
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, cf, Imm(0)); // TEMP = !CF
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, dest, zf, Imm(0)); // dest = !ZF
                            Add(instruction.IP, ISIL.OpCode.And, dest, dest, temp); // dest = !CF && !ZF
                            break;
                        case Mnemonic.Setbe:
                            Add(instruction.IP, ISIL.OpCode.Or, dest, cf, zf); // dest = CF || ZF
                            break;
                        case Mnemonic.Sets:
                            Add(instruction.IP, ISIL.OpCode.Move, dest, sf);
                            break;
                        case Mnemonic.Setns:
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, dest, sf, Imm(0));
                            break;
                        case Mnemonic.Setge:
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, dest, sf, of); // dest = SF == OF
                            break;
                        case Mnemonic.Setl:
                            Add(instruction.IP, ISIL.OpCode.CheckNotEqual, dest, sf, of); // dest = SF != OF
                            break;
                        case Mnemonic.Setg:
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, sf, of); // TEMP = SF == OF
                            Add(instruction.IP, ISIL.OpCode.CheckEqual, dest, zf, Imm(0)); // dest = !ZF
                            Add(instruction.IP, ISIL.OpCode.And, dest, dest, temp); // dest = !ZF && SF == OF
                            break;
                        case Mnemonic.Setle:
                            Add(instruction.IP, ISIL.OpCode.CheckNotEqual, temp, sf, of); // TEMP = SF != OF
                            Add(instruction.IP, ISIL.OpCode.Or, dest, temp, zf); // dest = ZF || SF != OF
                            break;
                    }
                    break;
                }

            case Mnemonic.Maxss: // dest < src ? src : dest
            case Mnemonic.Minss: // dest > src ? src : dest
                {
                    var dest = ConvertOperand(instruction, 0);
                    var src = ConvertOperand(instruction, 1);
                    AddCompareInstruction(instruction.IP, dest, src); // compare dest & src
                    if (instruction.Mnemonic == Mnemonic.Maxss)
                    {
                        var temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), temp); // enter if dest < src
                    }
                    else
                    {
                        var temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                        Add(instruction.IP, ISIL.OpCode.Or, temp, temp, new ISIL.Register(null, "ZF")); // TEMP = TEMP || ZF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), temp); // enter if dest > src
                    }

                    Add(instruction.IP, ISIL.OpCode.Move, dest, src); // dest = src
                    Add(instruction.IP + 1, ISIL.OpCode.Nop); // exit for IF
                    break;
                }

            case Mnemonic.Cmpxchg: // compare and exchange
                {
                    var accumulator = new ISIL.Register(null, instruction.Op1Register.GetSize() switch
                    {
                        8 => X86Utils.GetRegisterName(Register.RAX),
                        4 => X86Utils.GetRegisterName(Register.EAX),
                        2 => X86Utils.GetRegisterName(Register.AX),
                        1 => X86Utils.GetRegisterName(Register.AL),
                        _ => throw new NotSupportedException("unexpected behavior")
                    });
                    var dest = ConvertOperand(instruction, 0);
                    var src = ConvertOperand(instruction, 1);
                    AddCompareInstruction(instruction.IP, accumulator, dest); // compare dest & accumulator
                    Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "ZF")); // TEMP = !ZF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(instruction.IP + 1), new ISIL.Register(null, "TEMP")); // if accumulator == dest
                                                                                                                           // SET ZF = 1
                    Add(instruction.IP, ISIL.OpCode.Move, dest, src); // DEST = SRC
                    Add(instruction.IP, ISIL.OpCode.Jump, Imm(instruction.IP + 2)); // END IF
                                                                               // ELSE
                                                                               // SET ZF = 0
                    Add(instruction.IP + 1, ISIL.OpCode.Move, accumulator, dest); // accumulator = dest

                    Add(instruction.IP + 2, ISIL.OpCode.Nop); // exit for IF
                    break;
                }

            case Mnemonic.Jmp:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    var methodEnd = instruction.IP + (ulong)context.RawBytes.Length;
                    var methodStart = context.UnderlyingPointer;

                    if (jumpTarget < methodStart || jumpTarget > methodEnd)
                    {
                        callNoReturn = true;
                        goto case Mnemonic.Call;
                    }
                    else
                    {
                        Add(instruction.IP, ISIL.OpCode.Jump, Imm(jumpTarget));
                        break;
                    }
                }
                if (instruction.Op0Kind == OpKind.Register) // ex: jmp rax
                {
                    AddIndirectJmp(instruction);
                    break;
                }

                goto default;
            case Mnemonic.Je:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), new ISIL.Register(null, "ZF")); // if ZF == 1
                    break;
                }

                goto default;
            case Mnemonic.Jne:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "ZF")); // TEMP = !ZF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), new ISIL.Register(null, "TEMP"));
                    break;
                }
                goto default;
            case Mnemonic.Js:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), new ISIL.Register(null, "SF")); // if SF == 1
                    break;
                }

                goto default;
            case Mnemonic.Jns:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "SF")); // TEMP = !SF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), new ISIL.Register(null, "TEMP"));
                    break;
                }

                goto default;
            case Mnemonic.Jg:
            case Mnemonic.Ja:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;
                    var temp = new ISIL.Register(null, "TEMP");
                    var temp2 = new ISIL.Register(null, "TEMP2");

                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                    Add(instruction.IP, ISIL.OpCode.Not, temp2, new ISIL.Register(null, "ZF")); // TEMP2 = !ZF
                    Add(instruction.IP, ISIL.OpCode.And, temp, temp, temp2); // TEMP = TEMP && TEMP2
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), temp);
                    break;
                }

                goto default;
            case Mnemonic.Jl:
            case Mnemonic.Jb:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;
                    var temp = new ISIL.Register(null, "TEMP");

                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                    Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), temp);
                    break;
                }

                goto default;
            case Mnemonic.Jge:
            case Mnemonic.Jae:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;
                    var temp = new ISIL.Register(null, "TEMP");

                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), temp);
                    break;
                }

                goto default;
            case Mnemonic.Jle:
            case Mnemonic.Jbe:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;
                    var temp = new ISIL.Register(null, "TEMP");

                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                    Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                    Add(instruction.IP, ISIL.OpCode.Or, temp, temp, new ISIL.Register(null, "ZF")); // TEMP = TEMP || ZF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, Imm(jumpTarget), temp);
                    break;
                }

                goto default;
            case Mnemonic.Xchg:
                Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, "TEMP"), ConvertOperand(instruction, 0)); // TEMP = op0
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)); // op0 = op1
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 1), new ISIL.Register(null, "TEMP")); // op1 = TEMP
                break;
            case Mnemonic.Int:
            case Mnemonic.Int3:
                Add(instruction.IP, ISIL.OpCode.Interrupt); // We'll add it but eliminate later, can be used as a hint since compilers only emit it in normally unreachable code or in error handlers
                break;
            case Mnemonic.Prefetchw: // Fetches the cache line containing the specified byte from memory to the 1st or 2nd level cache, invalidating other cached copies.
            case Mnemonic.Nop:
                // While this is literally a nop and there's in theory no point emitting anything for it, it could be used as a jump target.
                // So we'll emit an ISIL nop for it.
                Add(instruction.IP, ISIL.OpCode.Nop);
                break;
            default:
                Add(instruction.IP, ISIL.OpCode.NotImplemented, new ISIL.StringLiteral(FormatInstruction(instruction)));
                break;
        }

        void AddCompareInstruction(ulong ip, ISIL.IOperand op0, ISIL.IOperand op1)
        {
            var temp1 = new ISIL.Register(null, "TEMP1");
            var temp2 = new ISIL.Register(null, "TEMP2");
            var temp3 = new ISIL.Register(null, "TEMP3");
            var temp4 = new ISIL.Register(null, "TEMP4");
            var temp5 = new ISIL.Register(null, "TEMP5");

            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "CF"), op0, op1); // CF = op1 < op2
            Add(ip, ISIL.OpCode.Subtract, temp1, op0, op1); // temp1 = op1 - op2
            Add(ip, ISIL.OpCode.Xor, temp2, op0, op1); // temp2 = op1 ^ op2
            Add(ip, ISIL.OpCode.Xor, temp3, op0, temp1); // temp3 = op1 ^ temp1
            Add(ip, ISIL.OpCode.And, temp4, temp2, temp3); // temp4 = temp2 & temp3
            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "OF"), temp4, Imm(0)); // OF = temp4 < 0
            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "SF"), temp1, Imm(0)); // SF = temp1 < 0
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "ZF"), temp1, Imm(0)); // ZF = temp1 == 0
            Add(ip, ISIL.OpCode.And, temp5, temp2, Imm(1)); // temp5 = tmp2 & 1
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "PF"), temp5, Imm(0)); // PF = temp5 == 0
        }

        void AddTestInstruction(ulong ip, ISIL.IOperand op0, ISIL.IOperand op1)
        {
            var temp = new ISIL.Register(null, "TEMP");
            var temp2 = new ISIL.Register(null, "TEMP2");
            var temp5 = new ISIL.Register(null, "TEMP5");

            Add(ip, ISIL.OpCode.And, temp, op0, op1); // temp = op0 & op1
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "ZF"), temp, Imm(0)); // ZF = temp == 0
            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "SF"), temp, Imm(0)); // SF = temp < 0
            Add(ip, ISIL.OpCode.Move, new ISIL.Register(null, "CF"), Imm(0));  // CF = 0
            Add(ip, ISIL.OpCode.Move, new ISIL.Register(null, "OF"), Imm(0));  // OF = 0
            Add(ip, ISIL.OpCode.Xor, temp2, temp, Imm(0)); // temp2 = temp ^ 0
            Add(ip, ISIL.OpCode.And, temp5, temp2, Imm(1)); // temp5 = temp2 & 1
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "PF"), temp5, Imm(0)); // PF = temp5 == 0
        }
    }

    private ISIL.IOperand ConvertScalarFloatOperand(Instruction instruction, int operand, bool single, MethodAnalysisContext? context)
    {
        if (context == null || instruction.GetOpKind(operand) != OpKind.Memory)
            return ConvertOperand(instruction, operand);

        if (!instruction.IsIPRelativeMemoryOperand && instruction is not { MemoryBase: Register.None, MemoryIndex: Register.None })
            return ConvertOperand(instruction, operand);

        var address = instruction.IsIPRelativeMemoryOperand ? instruction.IPRelativeMemoryAddress : instruction.MemoryDisplacement64;

        return ReadFloatConstant(context.AppContext.Binary, address, single) ?? ConvertOperand(instruction, operand);
    }

    private static ISIL.IOperand? ReadFloatConstant(LibCpp2IL.Il2CppBinary binary, ulong addr, bool single)
    {
        if (!binary.TryMapVirtualAddressToRaw(addr, out var raw))
            return null;

        var content = binary.GetRawBinaryContent();
        var size = single ? 4 : 8;

        if (raw < 0 || raw + size > content.Length)
            return null;

        var bytes = content.Slice((int)raw, size).ToArray();

        return single
            ? new ISIL.FloatLiteral(BitConverter.ToSingle(bytes, 0))
            : new ISIL.DoubleLiteral(BitConverter.ToDouble(bytes, 0));
    }

    private ISIL.IOperand ConvertOperand(Instruction instruction, int operand, bool isLeaAddress = false)
    {
        var kind = instruction.GetOpKind(operand);

        if (kind == OpKind.Register)
            return new ISIL.Register(null, X86Utils.GetRegisterName(instruction.GetOpRegister(operand)));
        if (kind.IsImmediate())
            return new ISIL.Immediate((long)instruction.GetImmediate(operand));
        if (kind == OpKind.Memory && instruction.MemoryBase == Register.RSP)
        {
            var slot = new ISIL.StackOffset((int)instruction.MemoryDisplacement32);
            return isLeaAddress ? new ISIL.AddressOf(slot) : slot;
        }

        //Memory
        //Most complex to least complex

        if (instruction.IsIPRelativeMemoryOperand)
            return new ISIL.MemoryOperand(addend: (long)instruction.IPRelativeMemoryAddress);

        //All four components
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 != 0)
        {
            var mBase = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryIndex));
            return new ISIL.MemoryOperand(mBase, mIndex, instruction.MemoryDisplacement32, instruction.MemoryIndexScale);
        }

        //No addend
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None)
        {
            var mBase = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryIndex));
            return new ISIL.MemoryOperand(mBase, mIndex, scale: instruction.MemoryIndexScale);
        }

        //No base
        if (instruction.MemoryIndex != Register.None && instruction.MemoryDisplacement64 != 0)
        {
            var mIndex = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryIndex));
            return new ISIL.MemoryOperand(null, mIndex, instruction.MemoryDisplacement32, instruction.MemoryIndexScale);
        }

        //No index (and so no scale)
        if (instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 > 0)
        {
            var mBase = new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryBase));
            return new ISIL.MemoryOperand(mBase, addend: (long)instruction.MemoryDisplacement64);
        }

        //Only base
        if (instruction.MemoryBase != Register.None)
        {
            return new ISIL.MemoryOperand(new ISIL.Register(null, X86Utils.GetRegisterName(instruction.MemoryBase)));
        }

        //Only addend
        return new ISIL.MemoryOperand(addend: (long)instruction.MemoryDisplacement64);
    }
}
