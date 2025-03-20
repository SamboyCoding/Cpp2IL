using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using Cpp2IL.Decompiler;
using Cpp2IL.Decompiler.IL;
using Iced.Intel;
using LibCpp2IL.BinaryStructures;
using Instruction = Iced.Intel.Instruction;
using Register = Iced.Intel.Register;

namespace Cpp2IL.Core.InstructionSets;

// This is honestly an X64InstructionSet by all means. Everything here screams "I AM X64".
public class X86InstructionSet : Cpp2IlInstructionSet
{
    private struct InstructionAddress(ulong address)
    {
        public ulong Address = address;
        public override string ToString() => $"@{Address}";
    }

    private static readonly MasmFormatter Formatter = new();
    private static readonly StringOutput Output = new();

    private static ConcurrentDictionary<string, int> _registerNumbers = [];

    private static object _carryFlag = CreateRegister("cf");
    private static object _overflowFlag = CreateRegister("of");
    private static object _signFlag = CreateRegister("sf");
    private static object _zeroFlag = CreateRegister("zf");
    private static object _parityFlag = CreateRegister("pf");
    private static object _xmm0 = CreateRegister("xmm0");
    private static object _rax = CreateRegister("rax");

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

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        lock (Formatter)
        {
            var insns = X86Utils.Iterate(X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, false), context.UnderlyingPointer);

            return string.Join("\n", insns.Select(FormatInstructionInternal));
        }
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var builder = new IsilBuilder();

        foreach (var instruction in X86Utils.Iterate(context))
        {
            ConvertInstructionStatement(instruction, builder, context);
        }

        builder.FixJumps();

        return builder.BackingStatementList;
    }


    private void ConvertInstructionStatement(Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        var callNoReturn = false;
        int operandSize;

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
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Cbw: // AX := sign-extend AL
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AX)),
                    InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AL)));
                break;
            case Mnemonic.Cwde: // EAX := sign-extend AX
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EAX)),
                    InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AX)));
                break;
            case Mnemonic.Cdqe: // RAX := sign-extend EAX
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RAX)),
                    InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EAX)));
                break;
            // it's very unsafe if there's been a jump to the next instruction here before.
            case Mnemonic.Cwd: // Convert Word to Doubleword
            {
                // The CWD instruction copies the sign (bit 15) of the value in the AX register into every bit position in the DX register
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.IP, temp, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.AX))); // TEMP = AX
                builder.ShiftRight(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(15)); // TEMP >>= 15
                builder.Compare(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(1)); // temp == 1
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                // temp == 1 ? DX := ushort.Max (1111111111) or DX := 0
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.DX)), InstructionSetIndependentOperand.MakeImmediate(ushort.MaxValue));
                builder.Goto(instruction.IP, instruction.IP + 2);
                builder.Move(instruction.IP + 1, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.DX)), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.Nop(instruction.IP + 2);
                break;
            }
            case Mnemonic.Cdq: // Convert Doubleword to Quadword
            {
                // The CDQ instruction copies the sign (bit 31) of the value in the EAX register into every bit position in the EDX register.
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.IP, temp, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EAX))); // TEMP = EAX
                builder.ShiftRight(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(31)); // TEMP >>= 31
                builder.Compare(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(1)); // temp == 1
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                // temp == 1 ? EDX := uint.Max (1111111111) or EDX := 0
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EDX)), InstructionSetIndependentOperand.MakeImmediate(uint.MaxValue));
                builder.Goto(instruction.IP, instruction.IP + 2);
                builder.Move(instruction.IP + 1, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.EDX)), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.Nop(instruction.IP + 2);
                break;
            }
            case Mnemonic.Cqo: // same...
            {
                // The CQO instruction copies the sign (bit 63) of the value in the EAX register into every bit position in the RDX register.
                var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                builder.Move(instruction.IP, temp, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RAX))); // TEMP = RAX
                builder.ShiftRight(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(63)); // TEMP >>= 63
                builder.Compare(instruction.IP, temp, InstructionSetIndependentOperand.MakeImmediate(1)); // temp == 1
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                // temp == 1 ? RDX := ulong.Max (1111111111) or RDX := 0
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RDX)), InstructionSetIndependentOperand.MakeImmediate(ulong.MaxValue));
                builder.Goto(instruction.IP, instruction.IP + 2);
                builder.Move(instruction.IP + 1, InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(Register.RDX)), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.Nop(instruction.IP + 2);
                break;
            }
            case Mnemonic.Lea:
                builder.LoadAddress(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Xor:
            case Mnemonic.Xorps: //xorps is just floating point xor
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                    builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));
                else
                    builder.Xor(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shl: // unsigned shift
            case Mnemonic.Sal: // signed shift
                builder.ShiftLeft(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shr: // unsigned shift
            case Mnemonic.Sar: // signed shift
                builder.ShiftRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.And:
            case Mnemonic.Andps: //Floating point and
                builder.And(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Or:
            case Mnemonic.Orps: //Floating point or
                builder.Or(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Not:
                builder.Neg(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Neg: // dest := -dest
                builder.Neg(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Imul:
                if (instruction.OpCount == 1)
                {
                    int opSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                    switch (opSize) // TODO: I don't know how to work with dual registers here, I left hints though
                    {
                        case 1: // Op0 * AL -> AX
                            builder.Multiply(instruction.IP, Register.AX.MakeIndependent(), ConvertOperand(instruction, 0), Register.AL.MakeIndependent());
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
                else if (instruction.OpCount == 3) builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));

                break;
            case Mnemonic.Mulss:
            case Mnemonic.Vmulss:
                if (instruction.OpCount == 3)
                    builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else if (instruction.OpCount == 2)
                    builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else
                    goto default;

                break;

            case Mnemonic.Divss: // Divide Scalar Single Precision Floating-Point Values. DEST[31:0] = DEST[31:0] / SRC[31:0]
                builder.Divide(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Vdivss: // VEX Divide Scalar Single Precision Floating-Point Values. DEST[31:0] = SRC1[31:0] / SRC2[31:0]
                builder.Divide(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Mnemonic.Ret:
                // TODO: Verify correctness of operation with Vectors.

                // On x32, this will require better engineering since ulongs are handled somehow differently (return in 2 registers, I think?)
                // The x64 prototype should work.
                // Are st* registers even used in il2cpp games?

                if (context.IsVoid)
                    builder.Return(instruction.IP);
                else if (context.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                    builder.Return(instruction.IP, InstructionSetIndependentOperand.MakeRegister("xmm0"));
                else
                    builder.Return(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rax"));
                break;
            case Mnemonic.Push:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.ShiftStack(instruction.IP, -operandSize);
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeStack(0), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Pop:
                operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeStack(0));
                builder.ShiftStack(instruction.IP, operandSize);
                break;
            case Mnemonic.Sub:
            case Mnemonic.Add:
                var isSubtract = instruction.Mnemonic == Mnemonic.Sub;

                // Special case - stack shift
                if (instruction.Op0Register == Register.RSP && instruction.Op1Kind.IsImmediate())
                {
                    var amount = (int)instruction.GetImmediate(1);
                    builder.ShiftStack(instruction.IP, isSubtract ? -amount : amount);
                    break;
                }

                var left = ConvertOperand(instruction, 0);
                var right = ConvertOperand(instruction, 1);
                if (isSubtract)
                    builder.Subtract(instruction.IP, left, left, right);
                else
                    builder.Add(instruction.IP, left, left, right);

                break;
            case Mnemonic.Addss:
            case Mnemonic.Subss:
            {
                // Addss and subss are just floating point add/sub, but we don't need to handle the stack stuff
                // But we do need to handle 2 vs 3 operand forms
                InstructionSetIndependentOperand dest;
                InstructionSetIndependentOperand src1;
                InstructionSetIndependentOperand src2;

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
                    builder.Subtract(instruction.IP, dest, src1, src2);
                else
                    builder.Add(instruction.IP, dest, src1, src2);
                break;
            }
            // The following pair of instructions does not update the Carry Flag (CF):
            case Mnemonic.Dec:
                builder.Subtract(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;
            case Mnemonic.Inc:
                builder.Add(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Shufps: // Packed Interleave Shuffle of Quadruplets of Single Precision Floating-Point Values
            {
                if (instruction.Op1Kind == OpKind.Memory)
                    goto default;

                var imm = instruction.Immediate8;
                var src1 = X86Utils.GetRegisterName(instruction.Op0Register);
                var src2 = X86Utils.GetRegisterName(instruction.Op1Register);
                var dest = "XMM_TEMP";
                //TEMP_DEST[31:0] := Select4(SRC1[127:0], imm8[1:0]);
                builder.Move(instruction.IP, ConvertVector(dest, 0), ConvertVector(src1, imm & 0b11));
                //TEMP_DEST[63:32] := Select4(SRC1[127:0], imm8[3:2]);
                builder.Move(instruction.IP, ConvertVector(dest, 1), ConvertVector(src1, (imm >> 2) & 0b11));
                //TEMP_DEST[95:64] := Select4(SRC2[127:0], imm8[5:4]);
                builder.Move(instruction.IP, ConvertVector(dest, 2), ConvertVector(src2, (imm >> 4) & 0b11));
                //TEMP_DEST[127:96] := Select4(SRC2[127:0], imm8[7:6]);
                builder.Move(instruction.IP, ConvertVector(dest, 3), ConvertVector(src2, (imm >> 6) & 0b11));
                // where Select4(regSlice, imm) => regSlice.[imm switch => { 0 => 0..31, 1 => 32..63, 2 => 64..95, 3 => 96...127 }];
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister(dest)); // DEST = TEMP_DEST
                break;

                static InstructionSetIndependentOperand ConvertVector(string reg, int imm) =>
                    InstructionSetIndependentOperand.MakeVectorElement(reg, IsilVectorRegisterElementOperand.VectorElementWidth.S, imm);
            }

            case Mnemonic.Unpcklps: // Unpack and Interleave Low Packed Single Precision Floating-Point Values
            {
                if (instruction.Op1Kind == OpKind.Memory)
                    goto default;

                var src1 = X86Utils.GetRegisterName(instruction.Op0Register);
                var src2 = X86Utils.GetRegisterName(instruction.Op1Register);
                var dest = "XMM_TEMP";
                builder.Move(instruction.IP, ConvertVector(dest, 0), ConvertVector(src1, 0)); //TMP_DEST[31:0] := SRC1[31:0]
                builder.Move(instruction.IP, ConvertVector(dest, 1), ConvertVector(src2, 0)); //TMP_DEST[63:32] := SRC2[31:0]
                builder.Move(instruction.IP, ConvertVector(dest, 2), ConvertVector(src1, 1)); //TMP_DEST[95:64] := SRC1[63:32]
                builder.Move(instruction.IP, ConvertVector(dest, 3), ConvertVector(src2, 1)); //TMP_DEST[127:96] := SRC2[63:32]
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister(dest)); // DEST = TEMP_DEST
                break;

                static InstructionSetIndependentOperand ConvertVector(string reg, int imm) =>
                    InstructionSetIndependentOperand.MakeVectorElement(reg, IsilVectorRegisterElementOperand.VectorElementWidth.S, imm);
            }

            case Mnemonic.Call:
                // We don't try and resolve which method is being called, but we do need to know how many parameters it has
                // I would hope that all of these methods have the same number of arguments, else how can they be inlined?

                var target = instruction.NearBranchTarget;

                if (instruction.Op0Kind == OpKind.Register)
                {
                    builder.CallRegister(instruction.IP, ConvertOperand(instruction, 0));
                }
                else if (context.AppContext.MethodsByAddress.TryGetValue(target, out var possibleMethods))
                {
                    if (possibleMethods.Count == 1)
                    {
                        builder.Call(instruction.IP, target, X64CallingConventionResolver.ResolveForManaged(possibleMethods[0]));
                    }
                    else
                    {
                        MethodAnalysisContext ctx = null!;
                        var lpars = -1;

                        // Very naive approach, folds with structs in parameters if GCC is used:
                        foreach (var method in possibleMethods)
                        {
                            var pars = method.ParameterCount;
                            if (method.IsStatic) pars++;
                            if (pars > lpars)
                            {
                                lpars = pars;
                                ctx = method;
                            }
                        }

                        // On post-analysis, you can discard methods according to the registers used, see X64CallingConventionResolver.
                        // This is less effective on GCC because MSVC doesn't overlap registers.

                        builder.Call(instruction.IP, target, X64CallingConventionResolver.ResolveForManaged(ctx));
                    }
                }
                else
                {
                    // This isn't a managed method, so for now we don't know its parameter count.
                    // This will need to be rewritten if we ever stumble upon an unmanaged method that accepts more than 4 parameters.
                    // These can be converted to dedicated ISIL instructions for specific API functions at a later stage. (by a post-processing step)

                    builder.Call(instruction.IP, target, X64CallingConventionResolver.ResolveForUnmanaged(context.AppContext, target));
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
                    builder.Compare(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));
                    break;
                }

                //Fall through to cmp, as test is just a cmp that doesn't set flags
                goto case Mnemonic.Cmp;
            case Mnemonic.Cmp:
            case Mnemonic.Comiss: //comiss is just a floating point compare dest[31:0] == src[31:0]
            case Mnemonic.Ucomiss: // same, but unsigned
                builder.Compare(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
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
                        builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1); // skip if not eq
                        break;
                    case Mnemonic.Cmovne: // not equals
                        builder.JumpIfEqual(instruction.IP, instruction.IP + 1); // skip if eq
                        break;
                    case Mnemonic.Cmovs: // sign
                        builder.JumpIfNotSign(instruction.IP, instruction.IP + 1); // skip if not sign
                        break;
                    case Mnemonic.Cmovns: // not sign
                        builder.JumpIfSign(instruction.IP, instruction.IP + 1); // skip if sign
                        break;
                    case Mnemonic.Cmova:
                    case Mnemonic.Cmovg: // greater
                        builder.JumpIfLessOrEqual(instruction.IP, instruction.IP + 1); // skip if not gt
                        break;
                    case Mnemonic.Cmovae:
                    case Mnemonic.Cmovge: // greater or eq
                        builder.JumpIfLess(instruction.IP, instruction.IP + 1); // skip if not gt or eq
                        break;
                    case Mnemonic.Cmovb:
                    case Mnemonic.Cmovl: // less
                        builder.JumpIfGreaterOrEqual(instruction.IP, instruction.IP + 1); // skip if not lt
                        break;
                    case Mnemonic.Cmovbe:
                    case Mnemonic.Cmovle: // less or eq
                        builder.JumpIfGreater(instruction.IP, instruction.IP + 1); // skip if not lt or eq
                        break;
                }

                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)); // set if cond
                builder.Nop(instruction.IP + 1);
                break;

            case Mnemonic.Maxss: // dest < src ? src : dest
            case Mnemonic.Minss: // dest > src ? src : dest
            {
                var dest = ConvertOperand(instruction, 0);
                var src = ConvertOperand(instruction, 1);
                builder.Compare(instruction.IP, dest, src); // compare dest & src
                if (instruction.Mnemonic == Mnemonic.Maxss)
                    builder.JumpIfGreaterOrEqual(instruction.IP, instruction.IP + 1); // enter if dest < src
                else
                    builder.JumpIfLessOrEqual(instruction.IP, instruction.IP + 1); // enter if dest > src
                builder.Move(instruction.IP, dest, src); // dest = src
                builder.Nop(instruction.IP + 1); // exit for IF
                break;
            }

            case Mnemonic.Cmpxchg: // compare and exchange
            {
                var accumulator = InstructionSetIndependentOperand.MakeRegister(instruction.Op1Register.GetSize() switch
                {
                    8 => X86Utils.GetRegisterName(Register.RAX),
                    4 => X86Utils.GetRegisterName(Register.EAX),
                    2 => X86Utils.GetRegisterName(Register.AX),
                    1 => X86Utils.GetRegisterName(Register.AL),
                    _ => throw new NotSupportedException("unexpected behavior")
                });
                var dest = ConvertOperand(instruction, 0);
                var src = ConvertOperand(instruction, 1);
                builder.Compare(instruction.IP, accumulator, dest);
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1); // if accumulator == dest
                // SET ZF = 1
                builder.Move(instruction.IP, dest, src); // DEST = SRC
                builder.Goto(instruction.IP, instruction.IP + 2); // END IF
                // ELSE
                // SET ZF = 0
                builder.Move(instruction.IP + 1, accumulator, dest); // accumulator = dest

                builder.Nop(instruction.IP + 2); // exit for IF
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
                        builder.Goto(instruction.IP, jumpTarget);
                        break;
                    }
                }

                if (instruction.Op0Kind == OpKind.Register) // ex: jmp rax
                {
                    builder.CallRegister(instruction.IP, ConvertOperand(instruction, 0), noReturn: true);
                    break;
                }

                goto default;
            case Mnemonic.Je:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jne:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfNotEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Js:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfSign(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jns:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfNotSign(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jg:
            case Mnemonic.Ja:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfGreater(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jl:
            case Mnemonic.Jb:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfLess(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jge:
            case Mnemonic.Jae:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfGreaterOrEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Jle:
            case Mnemonic.Jbe:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfLessOrEqual(instruction.IP, jumpTarget);
                    break;
                }

                goto default;
            case Mnemonic.Xchg:
                // There was supposed to be a push-mov-pop set but instructionAddress said no
                builder.Exchange(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Int:
            case Mnemonic.Int3:
                builder.Interrupt(instruction.IP); // We'll add it but eliminate later, can be used as a hint since compilers only emit it in normally unreachable code or in error handlers
                break;
            case Mnemonic.Prefetchw: // Fetches the cache line containing the specified byte from memory to the 1st or 2nd level cache, invalidating other cached copies.
            case Mnemonic.Nop:
                // While this is literally a nop and there's in theory no point emitting anything for it, it could be used as a jump target.
                // So we'll emit an ISIL nop for it.
                builder.Nop(instruction.IP);
                break;
            default:
                builder.NotImplemented(instruction.IP, FormatInstruction(instruction));
                break;
        }
    }


    private InstructionSetIndependentOperand ConvertOperand(Instruction instruction, int operand)
    {
        var kind = instruction.GetOpKind(operand);

        if (kind == OpKind.Register)
            return InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.GetOpRegister(operand)));
        if (kind.IsImmediate())
            return InstructionSetIndependentOperand.MakeImmediate(instruction.GetImmediate(operand));
        if (kind == OpKind.Memory && instruction.MemoryBase == Register.RSP)
            return InstructionSetIndependentOperand.MakeStack((int)instruction.MemoryDisplacement32);

        //Memory
        //Most complex to least complex

        if (instruction.IsIPRelativeMemoryOperand)
            return InstructionSetIndependentOperand.MakeMemory(new((long)instruction.IPRelativeMemoryAddress));

        //All four components
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 != 0)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryIndex));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, mIndex, instruction.MemoryDisplacement32, instruction.MemoryIndexScale));
        }

        //No addend
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryIndex));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, mIndex, instruction.MemoryIndexScale));
        }

        //No index (and so no scale)
        if (instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 > 0)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, (long)instruction.MemoryDisplacement64));
        }

        //Only base
        if (instruction.MemoryBase != Register.None)
        {
            return InstructionSetIndependentOperand.MakeMemory(new(InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase))));
        }

        //Only addend
        return InstructionSetIndependentOperand.MakeMemory(new((long)instruction.MemoryDisplacement64));
    }

    public override List<Decompiler.IL.Instruction> GetDecompilerIlFromMethod(MethodAnalysisContext context, out List<object> ilParams)
    {
        var isilParams = X64CallingConventionResolver.ResolveForManaged(context);
        ilParams = isilParams.Select(ConvertOperand).ToList();

        var addressMap = new List<(ulong, Decompiler.IL.Instruction)>();
        var instructions = new List<Decompiler.IL.Instruction>();
        var appContext = context.AppContext;
        var keyFunctionAddresses = appContext.GetOrCreateKeyFunctionAddresses();
        var isil = GetIsilFromMethod(context);

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

                    Add(new Decompiler.IL.Instruction(-1, opCode, operands[0], operands[1]), instruction);
                    break;
                case IsilMnemonic.ShiftLeft:
                case IsilMnemonic.ShiftRight:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.ShiftRight => OpCode.ShiftRight,
                        IsilMnemonic.ShiftLeft => OpCode.ShiftLeft
                    };

                    Add(new Decompiler.IL.Instruction(-1, opCode, operands[0], operands[0], operands[1]), instruction);
                    break;

                case IsilMnemonic.Call:
                case IsilMnemonic.CallNoReturn:
                    if (instruction.Operands[0].Data is IsilRegisterOperand)
                    {
                        Add(new Decompiler.IL.Instruction(-1, OpCode.Unknown, $"Indirect call: {instruction}"), instruction);
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
                        var target = (ulong)((IsilImmediateOperand)(instruction.Operands[0].Data)).Value;

                        if (keyFunctionAddresses.IsKeyFunctionAddress(target))
                            Add(new Decompiler.IL.Instruction(-1, OpCode.Unknown, GetKeyFunctionName(appContext, target, keyFunctionAddresses)), instruction);
                        else
                            Add(new Decompiler.IL.Instruction(-1, OpCode.Unknown, $"Method not found: {address:X}"), instruction);

                        break;
                    }

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

                    var call = new Decompiler.IL.Instruction(-1, opCode, calledMethod);

                    if (returnValue != null)
                        call.Operands.Insert(0, returnValue);

                    foreach (var arg in operands.Skip(1))
                        call.Operands.Add(arg);

                    Add(call, instruction);
                    break;

                case IsilMnemonic.Exchange:
                    // tmp = b, b = a, a = tmp
                    var exchangeTemp = CreateRegister("exchangeTemp");

                    Add(new Decompiler.IL.Instruction(-1, OpCode.Move, exchangeTemp, operands[1]), instruction); // tmp = b
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Move, operands[1], operands[0]), instruction); // b = a
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Move, operands[0], exchangeTemp), instruction); // a = tmp
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

                    Add(new Decompiler.IL.Instruction(-1, opCode, operands[0], operands[1], operands[2]), instruction);
                    break;

                case IsilMnemonic.Not:
                case IsilMnemonic.Neg:
                    opCode = instruction.OpCode.Mnemonic switch
                    {
                        IsilMnemonic.Not => OpCode.Not,
                        IsilMnemonic.Neg => OpCode.Negate
                    };

                    Add(new Decompiler.IL.Instruction(-1, opCode, operands[0], operands[0]), instruction);
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

                    Add(new Decompiler.IL.Instruction(-1, opCode, operands[0]), instruction);
                    break;

                case IsilMnemonic.Compare: // Set flags
                    var cmpA = operands[0];
                    var cmpB = operands[1];

                    var cmpTemp = CreateRegister("cmpTemp");

                    // cmpTemp = a - b
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Subtract, cmpTemp, cmpA, cmpB), instruction);
                    // CF = a < b
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckLess, _carryFlag, cmpA, cmpB), instruction);
                    // OF = cmpTemp > a
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckGreater, _overflowFlag, cmpTemp, cmpA), instruction);
                    // SF = cmpTemp < 0
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckLess, _signFlag, cmpTemp, 0), instruction);
                    // ZF = cmpTemp == 0
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckEqual, _zeroFlag, cmpTemp, 0), instruction);
                    // PF = cmpTemp & 1
                    Add(new Decompiler.IL.Instruction(-1, OpCode.And, _parityFlag, cmpTemp, 1), instruction);
                    break;

                case IsilMnemonic.Push:
                case IsilMnemonic.Pop:
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Unknown, $"Somehow Cpp2IL didn't translate {instruction} to ISIL!"), instruction);
                    break;

                case IsilMnemonic.Return:
                    Add(
                        instruction.Operands.Length == 0
                            ? new Decompiler.IL.Instruction(-1, OpCode.ReturnVoid)
                            : new Decompiler.IL.Instruction(-1, OpCode.Return, operands[0]),
                        instruction);
                    break;

                case IsilMnemonic.JumpIfEqual:
                    // ZF = 1
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], _zeroFlag), instruction);
                    break;

                case IsilMnemonic.JumpIfNotEqual:
                    // ZF = 0
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Not, CreateRegister("notZf"), _zeroFlag), instruction); // notZf = !zf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("notZf")), instruction);
                    break;

                case IsilMnemonic.JumpIfGreater:
                    // ZF = 0 & SF = OF
                    var zfIsZero = CreateRegister("zfIsZero");
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Not, zfIsZero, _zeroFlag), instruction); // zfIsZero = !zf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsOf"), _signFlag, _overflowFlag), instruction); // sfIsOf = sf == of
                    Add(new Decompiler.IL.Instruction(-1, OpCode.And, zfIsZero, CreateRegister("sfIsOf"), zfIsZero), instruction); // zfIsZero &= sfIsOf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], zfIsZero), instruction);
                    break;

                case IsilMnemonic.JumpIfGreaterOrEqual:
                    // SF = OF
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsOf"), _signFlag, _overflowFlag), instruction); // sfIsOf = sf == of
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("sfIsOf")), instruction);
                    break;

                case IsilMnemonic.JumpIfLess:
                    // SF != OF
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsNotOf"), _signFlag, _overflowFlag), instruction); // sfIsNotOf = sf == of
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Not, CreateRegister("sfIsNotOf"), CreateRegister("sfIsNotOf")), instruction); // sfIsNotOf = !sfIsNotOf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("sfIsNotOf")), instruction);
                    break;

                case IsilMnemonic.JumpIfLessOrEqual:
                    // ZF = 1 | SF != OF
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Move, CreateRegister("zfCopy"), _zeroFlag), instruction); // zfCopy = zf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.CheckEqual, CreateRegister("sfIsNotOf"), _signFlag, _overflowFlag), instruction); // sfIsNotOf = sf == of
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Not, CreateRegister("sfIsNotOf"), CreateRegister("sfIsNotOf")), instruction); // sfIsNotOf = !sfIsNotOf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Or, CreateRegister("zfCopy"), CreateRegister("sfIsNotOf"), CreateRegister("zfCopy")), instruction); // zfCopy |= sfIsNotOf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("zfCopy")), instruction);
                    break;

                case IsilMnemonic.JumpIfSign:
                    // SF = 1
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], _signFlag), instruction);
                    break;

                case IsilMnemonic.JumpIfNotSign:
                    // SF = 0
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Not, CreateRegister("notSf"), _signFlag), instruction); // notSf = !sf
                    Add(new Decompiler.IL.Instruction(-1, OpCode.ConditionalJump, operands[0], CreateRegister("notSf")), instruction);
                    break;

                case IsilMnemonic.SignExtend:
                    // IsilMnemonic.SignExtend is not used anywhere
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Unknown, $"SignExtend is not implemented ({instruction})"), instruction);
                    break;

                case IsilMnemonic.Interrupt:
                    if (context.IsVoid)
                        Add(new Decompiler.IL.Instruction(-1, OpCode.ReturnVoid), instruction);
                    else if (context.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                        Add(new Decompiler.IL.Instruction(-1, OpCode.Return, _xmm0), instruction);
                    else
                        Add(new Decompiler.IL.Instruction(-1, OpCode.Return, _rax), instruction);
                    break;

                case IsilMnemonic.Nop:
                    // These could be branch targets so these need to be added
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Nop), instruction);
                    break;

                default:
                    Add(new Decompiler.IL.Instruction(-1, OpCode.Unknown, $"Unknown instruction: {instruction}"),
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
                if (context.IsVoid)
                    instructions.Add(new Decompiler.IL.Instruction(-1, OpCode.ReturnVoid));
                else if (context.Definition?.RawReturnType?.Type is Il2CppTypeEnum.IL2CPP_TYPE_R4 or Il2CppTypeEnum.IL2CPP_TYPE_R8)
                    instructions.Add(new Decompiler.IL.Instruction(-1, OpCode.Return, _xmm0));
                else
                    instructions.Add(new Decompiler.IL.Instruction(-1, OpCode.Return, _rax));
            }
        }

        // Fix indexes
        for (var i = 0; i < instructions.Count; i++)
            instructions[i].Index = i;

        return instructions;

        void Add(Decompiler.IL.Instruction newInstruction, InstructionSetIndependentInstruction instruction)
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
                return new Decompiler.IL.Register(number, register.RegisterName);

            case IsilMemoryOperand memory:
                Decompiler.IL.Register? baseRegister = null;
                if (memory.Base != null)
                    baseRegister = (Decompiler.IL.Register)ConvertOperand((InstructionSetIndependentOperand)memory.Base!);

                Decompiler.IL.Register? index = null;
                if (memory.Index != null)
                    index = (Decompiler.IL.Register)ConvertOperand((InstructionSetIndependentOperand)memory.Index!);

                return new MemoryAddress(baseRegister, index, memory.Addend, memory.Scale);
            case IsilVectorRegisterElementOperand vectorRegisterElement:
                return ConvertOperand(InstructionSetIndependentOperand.MakeRegister(vectorRegisterElement.RegisterName));

            case InstructionSetIndependentInstruction instruction:
                return new InstructionAddress(instruction.ActualAddress);
        }

        return $"Unknown operand: {operand}";
    }

    private static string GetKeyFunctionName(ApplicationAnalysisContext appContext, ulong target, BaseKeyFunctionAddresses kFA)
    {
        var method = "";
        if (target == kFA.il2cpp_codegen_initialize_method || target == kFA.il2cpp_codegen_initialize_runtime_metadata)
        {
            if (appContext.MetadataVersion < 27)
                method = nameof(kFA.il2cpp_codegen_initialize_method);
            else
                method = nameof(kFA.il2cpp_codegen_initialize_runtime_metadata);
        }
        else if (target == kFA.il2cpp_vm_metadatacache_initializemethodmetadata)
            method = nameof(kFA.il2cpp_vm_metadatacache_initializemethodmetadata);
        else if (target == kFA.il2cpp_runtime_class_init_export)
            method = nameof(kFA.il2cpp_runtime_class_init_export);
        else if (target == kFA.il2cpp_runtime_class_init_actual)
            method = nameof(kFA.il2cpp_runtime_class_init_actual);
        else if (target == kFA.il2cpp_object_new)
            method = nameof(kFA.il2cpp_vm_object_new);
        else if (target == kFA.il2cpp_codegen_object_new)
            method = nameof(kFA.il2cpp_codegen_object_new);
        else if (target == kFA.il2cpp_array_new_specific)
            method = nameof(kFA.il2cpp_array_new_specific);
        else if (target == kFA.il2cpp_vm_array_new_specific)
            method = nameof(kFA.il2cpp_vm_array_new_specific);
        else if (target == kFA.SzArrayNew)
            method = nameof(kFA.SzArrayNew);
        else if (target == kFA.il2cpp_type_get_object)
            method = nameof(kFA.il2cpp_type_get_object);
        else if (target == kFA.il2cpp_vm_reflection_get_type_object)
            method = nameof(kFA.il2cpp_vm_reflection_get_type_object);
        else if (target == kFA.il2cpp_resolve_icall)
            method = nameof(kFA.il2cpp_resolve_icall);
        else if (target == kFA.InternalCalls_Resolve)
            method = nameof(kFA.InternalCalls_Resolve);
        else if (target == kFA.il2cpp_string_new)
            method = nameof(kFA.il2cpp_string_new);
        else if (target == kFA.il2cpp_vm_string_new)
            method = nameof(kFA.il2cpp_vm_string_new);
        else if (target == kFA.il2cpp_string_new_wrapper)
            method = nameof(kFA.il2cpp_string_new_wrapper);
        else if (target == kFA.il2cpp_vm_string_newWrapper)
            method = nameof(kFA.il2cpp_vm_string_newWrapper);
        else if (target == kFA.il2cpp_codegen_string_new_wrapper)
            method = nameof(kFA.il2cpp_codegen_string_new_wrapper);
        else if (target == kFA.il2cpp_value_box)
            method = nameof(kFA.il2cpp_value_box);
        else if (target == kFA.il2cpp_vm_object_box)
            method = nameof(kFA.il2cpp_vm_object_box);
        else if (target == kFA.il2cpp_object_unbox)
            method = nameof(kFA.il2cpp_object_unbox);
        else if (target == kFA.il2cpp_vm_object_unbox)
            method = nameof(kFA.il2cpp_vm_object_unbox);
        else if (target == kFA.il2cpp_raise_exception)
            method = nameof(kFA.il2cpp_raise_exception);
        else if (target == kFA.il2cpp_vm_exception_raise)
            method = nameof(kFA.il2cpp_vm_exception_raise);
        else if (target == kFA.il2cpp_codegen_raise_exception)
            method = nameof(kFA.il2cpp_codegen_raise_exception);
        else if (target == kFA.il2cpp_vm_object_is_inst)
            method = nameof(kFA.il2cpp_vm_object_is_inst);
        else if (target == kFA.AddrPInvokeLookup)
            method = nameof(kFA.AddrPInvokeLookup);

        return method;
    }
}
