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

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator, context.AppContext.Binary);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        lock (Formatter)
        {
            var insns = X86Utils.Iterate(X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, false, context.AppContext.Binary), context.UnderlyingPointer, context.AppContext.Binary.is32Bit);

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

            var targetAddress = (ulong)instruction.Operands[0];
            var targetIndex = addresses.FindIndex(addr => addr == targetAddress);

            if (targetIndex == -1)
            {
                instruction.OpCode = ISIL.OpCode.Invalid;
                instruction.Operands = [$"Jump target not found in method: 0x{targetAddress:X4}"];
                continue;
            }

            var targetInstruction = instructions[targetIndex];

            instruction.Operands[0] = targetInstruction;
        }

        return instructions;
    }

    public override List<object> GetParameterOperandsFromMethod(MethodAnalysisContext context)
    {
        return X64CallingConventionResolver.ResolveForManaged(context).ToList();
    }

    private void ConvertInstructionStatement(Instruction instruction, List<ISIL.Instruction> instructions, List<ulong> addresses, MethodAnalysisContext context)
    {
        var callNoReturn = false;
        int operandSize;

        ISIL.Instruction Add(ulong address, ISIL.OpCode opCode, params object[] operands)
        {
            addresses.Add(address);
            var newInstruction = new ISIL.Instruction(instructions.Count, opCode, operands);
            instructions.Add(newInstruction);
            return newInstruction;
        }

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov:
            case Mnemonic.Movzx: // For all intents and purposes we don't care about zero-extending
            case Mnemonic.Movsx: // move with sign-extendign
            case Mnemonic.Movsxd: // same
            case Mnemonic.Movaps: // Movaps is basically just a mov but with the potential future detail that the size is dependent on reg size
            case Mnemonic.Movups: // Movaps but unaligned
            case Mnemonic.Movss: // Same as movaps but for floats
            case Mnemonic.Movd: // Mov but specifically dword
            case Mnemonic.Movq: // Mov but specifically qword
            case Mnemonic.Movsd: // Mov but specifically double
            case Mnemonic.Movdqa: // Movaps but multiple integers at once in theory
            case Mnemonic.Cvtdq2ps: // Technically a convert double to single, but for analysis purposes we can just treat it as a move
            case Mnemonic.Cvtps2pd: // same, but float to double
            case Mnemonic.Cvttsd2si: // same, but double to integer
            case Mnemonic.Movdqu: // DEST[127:0] := SRC[127:0]
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
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
            // it's very unsafe if there's been a jump to the next instruction here before.
            case Mnemonic.Cwd: // Convert Word to Doubleword
                {
                    // The CWD instruction copies the sign (bit 15) of the value in the AX register into every bit position in the DX register
                    var temp = new ISIL.Register(null, "TEMP");
                    Add(instruction.IP, ISIL.OpCode.Move, temp, new ISIL.Register(null, X86Utils.GetRegisterName(Register.AX))); // TEMP = AX
                    Add(instruction.IP, ISIL.OpCode.ShiftRight, temp, temp, 15); // TEMP >>= 15
                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, temp, 1); // temp == 1
                    Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // temp = !temp
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp);
                    // temp == 1 ? DX := ushort.Max (1111111111) or DX := 0
                    Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.DX)), ushort.MaxValue);
                    Add(instruction.IP, ISIL.OpCode.Jump, instruction.IP + 2);
                    Add(instruction.IP + 1, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.DX)), 0);
                    Add(instruction.IP + 2, ISIL.OpCode.Nop);
                    break;
                }
            case Mnemonic.Cdq: // Convert Doubleword to Quadword
                {
                    // The CDQ instruction copies the sign (bit 31) of the value in the EAX register into every bit position in the EDX register.
                    var temp = new ISIL.Register(null, "TEMP");
                    Add(instruction.IP, ISIL.OpCode.Move, temp, new ISIL.Register(null, X86Utils.GetRegisterName(Register.EAX))); // TEMP = EAX
                    Add(instruction.IP, ISIL.OpCode.ShiftRight, temp, temp, 31); // TEMP >>= 31
                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, temp, 1); // temp == 1
                    Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // temp = !temp
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp);
                    // temp == 1 ? EDX := uint.Max (1111111111) or EDX := 0
                    Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.EDX)), uint.MaxValue);
                    Add(instruction.IP, ISIL.OpCode.Jump, instruction.IP + 2);
                    Add(instruction.IP + 1, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.EDX)), 0);
                    Add(instruction.IP + 2, ISIL.OpCode.Nop);
                    break;
                }
            case Mnemonic.Cqo: // same...
                {
                    // The CQO instruction copies the sign (bit 63) of the value in the EAX register into every bit position in the RDX register.
                    var temp = new ISIL.Register(null, "TEMP");
                    Add(instruction.IP, ISIL.OpCode.Move, temp, new ISIL.Register(null, X86Utils.GetRegisterName(Register.RAX))); // TEMP = RAX
                    Add(instruction.IP, ISIL.OpCode.ShiftRight, temp, temp, 63); // TEMP >>= 63
                    Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, temp, 1); // temp == 1
                    Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // temp = !temp
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp);
                    // temp == 1 ? RDX := ulong.Max (1111111111) or RDX := 0
                    Add(instruction.IP, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.RDX)), ulong.MaxValue);
                    Add(instruction.IP, ISIL.OpCode.Jump, instruction.IP + 2);
                    Add(instruction.IP + 1, ISIL.OpCode.Move, new ISIL.Register(null, X86Utils.GetRegisterName(Register.RDX)), 0);
                    Add(instruction.IP + 2, ISIL.OpCode.Nop);
                    break;
                }
            case Mnemonic.Lea:
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1, true));
                break;
            case Mnemonic.Xor:
            case Mnemonic.Xorps: //xorps is just floating point xor
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                    Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), 0);
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
            case Mnemonic.Mulss:
            case Mnemonic.Vmulss:
                if (instruction.OpCount == 3)
                    Add(instruction.IP, ISIL.OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else if (instruction.OpCount == 2)
                    Add(instruction.IP, ISIL.OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else
                    goto default;

                break;

            case Mnemonic.Divss: // Divide Scalar Single Precision Floating-Point Values. DEST[31:0] = DEST[31:0] / SRC[31:0]
                Add(instruction.IP, ISIL.OpCode.Divide, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Vdivss: // VEX Divide Scalar Single Precision Floating-Point Values. DEST[31:0] = SRC1[31:0] / SRC2[31:0]
                Add(instruction.IP, ISIL.OpCode.Divide, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
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
                Add(instruction.IP, ISIL.OpCode.ShiftStack, -operandSize);
                Add(instruction.IP, ISIL.OpCode.Move, new ISIL.StackOffset(0), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Pop:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), new ISIL.StackOffset(0));
                Add(instruction.IP, ISIL.OpCode.ShiftStack, operandSize);
                break;
            case Mnemonic.Sub:
            case Mnemonic.Add:
                var isSubtract = instruction.Mnemonic == Mnemonic.Sub;

                // Special case - stack shift
                if (instruction.Op0Register == Register.RSP && instruction.Op1Kind.IsImmediate())
                {
                    var amount = (int)instruction.GetImmediate(1);
                    Add(instruction.IP, ISIL.OpCode.ShiftStack, isSubtract ? -amount : amount);
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
                    object dest;
                    object src1;
                    object src2;

                    if (instruction.OpCount == 3)
                    {
                        //dest, src1, src2
                        dest = ConvertOperand(instruction, 0);
                        src1 = ConvertOperand(instruction, 1);
                        src2 = ConvertOperand(instruction, 2);
                    }
                    else if (instruction.OpCount == 2)
                    {
                        //DestAndSrc1, Src2
                        dest = ConvertOperand(instruction, 0);
                        src1 = dest;
                        src2 = ConvertOperand(instruction, 1);
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
                Add(instruction.IP, ISIL.OpCode.Subtract, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), 1);
                break;
            case Mnemonic.Inc:
                Add(instruction.IP, ISIL.OpCode.Add, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), 1);
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
                    Add(instruction.IP, ISIL.OpCode.IndirectCall, ConvertOperand(instruction, 0));
                }
                else if (context.AppContext.MethodsByAddress.TryGetValue(target, out var possibleMethods))
                {
                    if (possibleMethods.Count == 1)
                    {
                        ISIL.Instruction call;

                        if (possibleMethods[0].IsVoid)
                            call = Add(instruction.IP, ISIL.OpCode.CallVoid, target);
                        else
                            call = Add(instruction.IP, ISIL.OpCode.Call, target, new ISIL.Register(null, "rax") /* return value */);

                        call.Operands.AddRange(X64CallingConventionResolver.ResolveForManaged(possibleMethods[0]));
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

                        // On post-analysis, you can discard methods according to the registers used, see X64CallingConventionResolver.
                        // This is less effective on GCC because MSVC doesn't overlap registers.

                        ISIL.Instruction call;

                        if (ctx.IsVoid)
                            call = Add(instruction.IP, ISIL.OpCode.CallVoid, target);
                        else
                            call = Add(instruction.IP, ISIL.OpCode.Call, target, new ISIL.Register(null, "rax") /* return value */);

                        call.Operands.AddRange(X64CallingConventionResolver.ResolveForManaged(ctx));
                    }
                }
                else
                {
                    // This isn't a managed method, so for now we don't know its parameter count.
                    // This will need to be rewritten if we ever stumble upon an unmanaged method that accepts more than 4 parameters.
                    // These can be converted to dedicated ISIL instructions for specific API functions at a later stage. (by a post-processing step)

                    var call = Add(instruction.IP, ISIL.OpCode.Call, target, new ISIL.Register(null, "rax") /* return value */);
                    call.Operands.AddRange(X64CallingConventionResolver.ResolveForUnmanaged(context.AppContext, target));
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
                    AddCompareInstruction(instruction.IP, ConvertOperand(instruction, 0), 0);
                    break;
                }
                AddTestInstruction(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Cmp:
            case Mnemonic.Comiss: //comiss is just a floating point compare dest[31:0] == src[31:0]
            case Mnemonic.Ucomiss: // same, but unsigned
                AddCompareInstruction(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
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
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, new ISIL.Register(null, "TEMP")); // skip if not eq
                        break;
                    case Mnemonic.Cmovne: // not equals
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, new ISIL.Register(null, "ZF")); // skip if eq
                        break;
                    case Mnemonic.Cmovs: // sign
                        Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "SF")); // TEMP = !SF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, new ISIL.Register(null, "TEMP")); // skip if not sign
                        break;
                    case Mnemonic.Cmovns: // not sign
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, new ISIL.Register(null, "SF")); // skip if sign
                        break;
                    case Mnemonic.Cmova:
                    case Mnemonic.Cmovg: // greater
                        var temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                        Add(instruction.IP, ISIL.OpCode.Or, temp, temp, new ISIL.Register(null, "ZF")); // TEMP = TEMP || ZF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp); // skip if not gt
                        break;
                    case Mnemonic.Cmovae:
                    case Mnemonic.Cmovge: // greater or eq
                        temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp); // skip if not gt or eq
                        break;
                    case Mnemonic.Cmovb:
                    case Mnemonic.Cmovl: // less
                        temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp); // skip if not lt
                        break;
                    case Mnemonic.Cmovbe:
                    case Mnemonic.Cmovle: // less or eq
                        temp = new ISIL.Register(null, "TEMP");
                        var temp2 = new ISIL.Register(null, "TEMP2");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp2, new ISIL.Register(null, "ZF")); // TEMP2 = !ZF
                        Add(instruction.IP, ISIL.OpCode.And, temp, temp, temp2); // TEMP = TEMP && TEMP2
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp); // skip if not lt or eq
                        break;
                }
                Add(instruction.IP, ISIL.OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)); // set if cond
                Add(instruction.IP + 1, ISIL.OpCode.Nop);
                break;

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
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp); // enter if dest < src
                    }
                    else
                    {
                        var temp = new ISIL.Register(null, "TEMP");
                        Add(instruction.IP, ISIL.OpCode.CheckEqual, temp, new ISIL.Register(null, "SF"), new ISIL.Register(null, "OF")); // TEMP = SF == OF
                        Add(instruction.IP, ISIL.OpCode.Not, temp, temp); // TEMP = !TEMP
                        Add(instruction.IP, ISIL.OpCode.Or, temp, temp, new ISIL.Register(null, "ZF")); // TEMP = TEMP || ZF
                        Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, temp); // enter if dest > src
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
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, instruction.IP + 1, new ISIL.Register(null, "TEMP")); // if accumulator == dest
                                                                                                                           // SET ZF = 1
                    Add(instruction.IP, ISIL.OpCode.Move, dest, src); // DEST = SRC
                    Add(instruction.IP, ISIL.OpCode.Jump, instruction.IP + 2); // END IF
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
                        Add(instruction.IP, ISIL.OpCode.Jump, jumpTarget);
                        break;
                    }
                }
                if (instruction.Op0Kind == OpKind.Register) // ex: jmp rax
                {
                    Add(instruction.IP, ISIL.OpCode.IndirectCall, ConvertOperand(instruction, 0));
                    break;
                }

                goto default;
            case Mnemonic.Je:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, new ISIL.Register(null, "ZF")); // if ZF == 1
                    break;
                }

                goto default;
            case Mnemonic.Jne:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "ZF")); // TEMP = !ZF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, new ISIL.Register(null, "TEMP"));
                    break;
                }
                goto default;
            case Mnemonic.Js:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, new ISIL.Register(null, "SF")); // if SF == 1
                    break;
                }

                goto default;
            case Mnemonic.Jns:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    Add(instruction.IP, ISIL.OpCode.Not, new ISIL.Register(null, "TEMP"), new ISIL.Register(null, "SF")); // TEMP = !SF
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, new ISIL.Register(null, "TEMP"));
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
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, temp);
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
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, temp);
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
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, temp);
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
                    Add(instruction.IP, ISIL.OpCode.ConditionalJump, jumpTarget, temp);
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
                Add(instruction.IP, ISIL.OpCode.NotImplemented, FormatInstruction(instruction));
                break;
        }

        void AddCompareInstruction(ulong ip, object op0, object op1)
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
            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "OF"), temp4, 0); // OF = temp4 < 0
            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "SF"), temp1, 0); // SF = temp1 < 0
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "ZF"), temp1, 0); // ZF = temp1 == 0
            Add(ip, ISIL.OpCode.And, temp5, temp2, 1); // temp5 = tmp2 & 1
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "PF"), temp5, 0); // PF = temp5 == 0
        }

        void AddTestInstruction(ulong ip, object op0, object op1)
        {
            var temp = new ISIL.Register(null, "TEMP");
            var temp2 = new ISIL.Register(null, "TEMP2");
            var temp5 = new ISIL.Register(null, "TEMP5");

            Add(ip, ISIL.OpCode.And, temp, op0, op1); // temp = op0 & op1
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "ZF"), temp, 0); // ZF = temp == 0
            Add(ip, ISIL.OpCode.CheckLess, new ISIL.Register(null, "SF"), temp, 0); // SF = temp < 0
            Add(ip, ISIL.OpCode.Move, new ISIL.Register(null, "CF"), 0);  // CF = 0
            Add(ip, ISIL.OpCode.Move, new ISIL.Register(null, "OF"), 0);  // OF = 0
            Add(ip, ISIL.OpCode.Xor, temp2, temp, 0); // temp2 = temp ^ 0
            Add(ip, ISIL.OpCode.And, temp5, temp2, 1); // temp5 = temp2 & 1
            Add(ip, ISIL.OpCode.CheckEqual, new ISIL.Register(null, "PF"), temp5, 0); // PF = temp5 == 0
        }
    }


    private object ConvertOperand(Instruction instruction, int operand, bool isLeaAddress = false)
    {
        var kind = instruction.GetOpKind(operand);

        if (kind == OpKind.Register)
            return new ISIL.Register(null, X86Utils.GetRegisterName(instruction.GetOpRegister(operand)));
        if (kind.IsImmediate())
            return instruction.GetImmediate(operand);
        if (kind == OpKind.Memory && instruction.MemoryBase == Register.RSP)
            return new ISIL.StackOffset((int)instruction.MemoryDisplacement32);

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
            return new ISIL.MemoryOperand(mBase, mIndex, instruction.MemoryIndexScale);
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
