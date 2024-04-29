using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.InstructionSets;

public class X86InstructionSet : Cpp2IlInstructionSet
{

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        var insns = X86Utils.Disassemble(X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, false), context.UnderlyingPointer);

        return string.Join("\n", insns);
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var insns = X86Utils.Disassemble(context.RawBytes, context.UnderlyingPointer);

        var builder = new IsilBuilder();

        foreach (var instruction in insns)
        {
            ConvertInstructionStatement(instruction, builder, context);
        }

        builder.FixJumps();

        return builder.BackingStatementList;
    }


    private void ConvertInstructionStatement(Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
        // var callNoReturn = false; // stub, see case Mnemonic.Call
        
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov:
            case Mnemonic.Movq:
            case Mnemonic.Movdqa:
            case Mnemonic.Movdqu:
            case Mnemonic.Movd:
            case Mnemonic.Movss: // Move or Merge Scalar Single Precision Floating-Point Value. Bad way
            case Mnemonic.Movapd:
            case Mnemonic.Movaps:
            case Mnemonic.Movupd:
            case Mnemonic.Movups:
            case Mnemonic.Movsd:
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Movzx:
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.ZeroExtend(instruction.IP+1, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Movsxd:
            case Mnemonic.Movsx:
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.SignExtend(instruction.IP + 1, ConvertOperand(instruction, 0));
                break;

            // condiontal mov
            case Mnemonic.Cmovl:
            case Mnemonic.Cmova: // set if above
                builder.JumpIfLessOrEqual(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;
            case Mnemonic.Cmovle:
            case Mnemonic.Cmovae: // set if above or eq
                builder.JumpIfLess(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;
            case Mnemonic.Cmovg:
            case Mnemonic.Cmovb: // set if below
                builder.JumpIfGreater(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;
            case Mnemonic.Cmovge:
            case Mnemonic.Cmovbe: // set if below or eq
                builder.JumpIfGreaterOrEqual(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;
            case Mnemonic.Cmove: // set if equal
                builder.JumpIfNotEqual(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;
            case Mnemonic.Cmovne: // set if not equal
                builder.JumpIfEqual(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;

            case Mnemonic.Xadd:
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister("TEMP"), ConvertOperand(instruction, 0));
                builder.Add(instruction.IP, InstructionSetIndependentOperand.MakeRegister("TEMP"), ConvertOperand(instruction, 1)); // TEMP = SRC + DEST
                builder.Move(instruction.IP, ConvertOperand(instruction, 1), ConvertOperand(instruction, 0)); // SRC = DEST
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("TEMP")); // DEST = TEMP
                break;

            case Mnemonic.Mulsd:
            case Mnemonic.Mulss:
                builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;

            case Mnemonic.Lea:
                builder.LoadAddress(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Xor:
            case Mnemonic.Xorpd:
            case Mnemonic.Xorps:
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                    builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));
                else
                    builder.Xor(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Neg:
                builder.Neg(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Shl:
                builder.ShiftLeft(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shr:
                builder.ShiftRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;

            case Mnemonic.Andps:
            case Mnemonic.And:
                builder.And(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Or:
                builder.Or(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Not:
                builder.Not(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Imul:
                if (instruction.OpCount == 1)
                {
                    int OpSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                    switch (OpSize) // TODO I don't know how to work with dual registers here in Iced, I left hints though
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
            case Mnemonic.Idiv: // (Quotient, Remainder) = (E/R)AX / operand;
                builder.Divide(instruction.IP, ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Divss:
                if (instruction.OpCount == 2)
                    builder.Divide(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else
                    builder.Divide(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Mnemonic.Ret:
                if (context.IsVoid)
                    builder.Return(instruction.IP);
                else
                    builder.Return(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rax")); //TODO Support xmm0
                break;
            case Mnemonic.Push:
                //var operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.Push(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rsp"), ConvertOperand(instruction, 0));
                //builder.ShiftStack(instruction.IP, -operandSize);
                break;
            case Mnemonic.Pusha:
                var rsp = InstructionSetIndependentOperand.MakeRegister("rsp");
                builder.Push(instruction.IP, rsp, InstructionSetIndependentOperand.MakeRegister("eax"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("ecx"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("edx"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("ebx"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("esp"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("ebp"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("esi"));
                builder.Push(instruction.IP + 1, rsp, InstructionSetIndependentOperand.MakeRegister("edi"));
                break;
            case Mnemonic.Pushf:
                builder.Push(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rsp"), InstructionSetIndependentOperand.MakeRegister("FLAGS"));
                break;
            case Mnemonic.Pop:
                //var operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                //builder.ShiftStack(instruction.IP, operandSize);
                builder.Pop(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rsp"), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Popf:
                builder.Pop(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rsp"), InstructionSetIndependentOperand.MakeRegister("FLAGS"));
                break;
            case Mnemonic.Popa:
                var _rsp = InstructionSetIndependentOperand.MakeRegister("rsp");
                builder.Pop(instruction.IP, _rsp, InstructionSetIndependentOperand.MakeRegister("eax"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("ecx"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("edx"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("ebx"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("esp"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("ebp"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("esi"));
                builder.Pop(instruction.IP + 1, _rsp, InstructionSetIndependentOperand.MakeRegister("edi"));
                break;
            case Mnemonic.Sub:
            case Mnemonic.Subss:
            case Mnemonic.Subsd:
            case Mnemonic.Adc: // also sets CF
            case Mnemonic.Add:
            case Mnemonic.Addsd:
            case Mnemonic.Addss:
                var isSubtract = instruction.Mnemonic is Mnemonic.Sub or Mnemonic.Subsd or Mnemonic.Subss;

                //Special case - stack shift
                if (instruction.Op0Register == Register.RSP && instruction.Op1Kind.IsImmediate())
                {
                    var amount = (int) instruction.GetImmediate(1);
                    builder.ShiftStack(instruction.IP, isSubtract ? -amount : amount);
                    break;
                }

                var left = ConvertOperand(instruction, 0);
                var right = ConvertOperand(instruction, 1);
                if (isSubtract)
                    builder.Subtract(instruction.IP, left, right);
                else
                    builder.Add(instruction.IP, left, right);

                break;
            case Mnemonic.Dec:
            case Mnemonic.Inc:
                // no CF
                var isDec = instruction.Mnemonic == Mnemonic.Dec;
                var im = InstructionSetIndependentOperand.MakeImmediate(1);
                if (isDec) builder.Subtract(instruction.IP, ConvertOperand(instruction, 0), im);
                else builder.Add(instruction.IP, ConvertOperand(instruction, 0), im);
                break;
            case Mnemonic.Call:
                // We don't try and resolve which method is being called, but we do need to know how many parameters it has
                // I would hope that all of these methods have the same number of arguments, else how can they be inlined?
                // TODO: Handle CallNoReturn(I have no idea how due to instructionAddress constantly being a limitation)
                var target = instruction.NearBranchTarget;
                if (context.AppContext.MethodsByAddress.ContainsKey(target))
                {
                    var possibleMethods = context.AppContext.MethodsByAddress[target];
                    var parameterCounts = possibleMethods.Select(p =>
                    {
                        var ret = p.Parameters.Count;
                        if (!p.IsStatic)
                            ret++; //This arg

                        ret++; //For MethodInfo arg
                        return ret;
                    }).ToArray();

                    // if (parameterCounts.Max() != parameterCounts.Min())
                    // throw new("Cannot handle call to address with multiple managed methods of different parameter counts");

                    var parameterCount = parameterCounts.Max();
                    var registerParams = new[] { "rcx", "rdx", "r8", "r9" }.Select(InstructionSetIndependentOperand.MakeRegister).ToList();

                    if (parameterCount <= registerParams.Count)
                    {
                        builder.Call(instruction.IP, target, registerParams.GetRange(0, parameterCount).ToArray());
                        return;
                    }

                    //Need to use stack
                    parameterCount -= registerParams.Count; //Subtract the 4 params we can fit in registers

                    //Generate and append stack operands
                    var ptrSize = (int) context.AppContext.Binary.PointerSize;
                    registerParams = registerParams.Concat(Enumerable.Range(0, parameterCount).Select(p => p * ptrSize).Select(InstructionSetIndependentOperand.MakeStack)).ToList();

                    builder.Call(instruction.IP, target, registerParams.ToArray());

                    //Discard the consumed stack space
                    builder.ShiftStack(instruction.IP, -parameterCount * 8);
                }
                else
                {
                    //This isn't a managed method, so for now we don't know its parameter count.
                    //Add all four of the registers, I guess. If there are any functions that take more than 4 params,
                    //we'll have to do something else here.
                    //These can be converted to dedicated ISIL instructions for specific API functions at a later stage. (by a post-processing step)
                    var paramRegisters = new[] { "rcx", "rdx", "r8", "r9" }.Select(InstructionSetIndependentOperand.MakeRegister).ToArray();
                    builder.Call(instruction.IP, target, paramRegisters);
                }
                break;
            case Mnemonic.Test:
                if (instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register && instruction.Op0Register == instruction.Op1Register)
                {
                    builder.Compare(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));
                    break;
                }
                builder.Compare(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Ucomisd: // Unordered Compare Scalar Double Precision Floating-Point Values and Set EFLAGS
            case Mnemonic.Ucomiss: // Unordered Compare Scalar Single Precision Floating-Point Values and Set EFLAGS
            case Mnemonic.Comiss:
            case Mnemonic.Comisd:
            case Mnemonic.Cmp:
                builder.Compare(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Jmp:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    var methodEnd = instruction.IP + (ulong) context.RawBytes.Length;
                    var methodStart = context.UnderlyingPointer;

                    if (jumpTarget < methodStart || jumpTarget > methodEnd)
                    {
                        // callNoReturn = true;
                        goto case Mnemonic.Call; // This is like 99% likely a non returning call, jump to case to avoid code duplication
                    }
                    else
                    {
                        builder.Goto(instruction.IP, jumpTarget);
                        break;
                    }
                }
                builder.Goto(instruction.IP, ConvertOperand(instruction, 0));
                break;
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
            case Mnemonic.Jp:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    builder.JumpIfParity(instruction.IP, jumpTarget);
                    break;
                }
                goto default;
            case Mnemonic.Js:
                builder.JumpIfSign(instruction.IP, instruction.NearBranchTarget);
                break;
            case Mnemonic.Jns:
                builder.JumpIfNotSign(instruction.IP, instruction.NearBranchTarget);
                break;
            case Mnemonic.Xchg:
                // There was supposed to be a push-mov-pop set but instructionAddress said no
                builder.Exchange(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Int:
            case Mnemonic.Int3:
                builder.Interrupt(instruction.IP); // We'll add it but eliminate later
                break;

            case Mnemonic.Loop:
                builder.Subtract(instruction.IP, InstructionSetIndependentOperand.MakeRegister("ECX"), InstructionSetIndependentOperand.MakeImmediate(1));
                builder.Compare(instruction.IP+1, InstructionSetIndependentOperand.MakeRegister("ECX"), InstructionSetIndependentOperand.MakeImmediate(0));
                builder.JumpIfNotEqual(instruction.IP+1, instruction.NearBranchTarget);
                break;

            case Mnemonic.Psrad: // Arithmetic
            case Mnemonic.Psraw:
            case Mnemonic.Psrld: // logical
            case Mnemonic.Psrldq:
            case Mnemonic.Psrlq:
            case Mnemonic.Psrlw:
                if (instruction.OpCount == 2) // src = src >> op
                    builder.ShiftRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else // dst = src >> op
                    goto default;
                break;

            case Mnemonic.Pslld:
            case Mnemonic.Pslldq:
            case Mnemonic.Psllq:
            case Mnemonic.Psllw:
                if (instruction.OpCount == 2)
                    builder.ShiftRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else
                    goto default;
                break;

            case Mnemonic.Shufpd:
            case Mnemonic.Shufps:
                if (instruction.OpCount != 4)
                    builder.Shuffle(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 2), ConvertOperand(instruction, 3));
                else
                    builder.Shuffle(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 2), ConvertOperand(instruction, 3), ConvertOperand(instruction, 4));
                break;

            case Mnemonic.Cvtdq2pd:
            case Mnemonic.Cvtpi2pd:
            case Mnemonic.Cvtsi2sd:
                builder.Convert(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), (typeof(int[]), typeof(double[])));
                break;
            case Mnemonic.Cvtdq2ps:
            case Mnemonic.Cvtpi2ps:
            case Mnemonic.Cvtsi2ss:
                builder.Convert(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), (typeof(int[]), typeof(float[])));
                break;
            case Mnemonic.Cvtpd2pi: // whut
            case Mnemonic.Cvtpd2dq:
            case Mnemonic.Cvtsd2si:
                builder.Convert(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), (typeof(double[]), typeof(int[])));
                break;
            case Mnemonic.Cvtsd2ss:
            case Mnemonic.Cvtpd2ps:
                builder.Convert(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), (typeof(double[]), typeof(float[])));
                break;
            case Mnemonic.Cvtps2pi:
            case Mnemonic.Cvtps2dq:
            case Mnemonic.Cvtss2si:
                builder.Convert(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), (typeof(float[]), typeof(int[])));
                break;
            case Mnemonic.Cvtps2pd:
                builder.Convert(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), (typeof(float[]), typeof(double[])));
                break;

            case Mnemonic.Bt: // cursed bit things
                builder.BitTest(instruction.IP, BitTestType.BitTest, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Btc:
                builder.BitTest(instruction.IP, BitTestType.BitTestAndComplement, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Btr:
                builder.BitTest(instruction.IP, BitTestType.BitTestAndReset, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Bts: //BTS? here they are from left to right!
                builder.BitTest(instruction.IP, BitTestType.BitTestAndSet, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;

            case Mnemonic.Prefetchw: // Prefetch Data Into Caches
            case Mnemonic.Prefetch: // cursed thing
            case Mnemonic.Nop: // nop for nop
                builder.Nop(instruction.IP);
                break;

            case Mnemonic.Setl:
                builder.CursedCPUFlags(instruction.IP, ConvertOperand(instruction, 0), "dst = SF!=OF");
                break;
            case Mnemonic.Setle: // dst = ZF==1 or SF!=OF
                builder.CursedCPUFlags(instruction.IP, ConvertOperand(instruction, 0), "dst = ZF==1 or SF!=OF");
                break;
            case Mnemonic.Seta: // dst = CF==ZF==0 
                builder.CursedCPUFlags(instruction.IP, ConvertOperand(instruction, 0), "dst = CF==ZF==0 ");
                break;
            case Mnemonic.Setg: // dst = ZF==0 or SF==OF
                builder.CursedCPUFlags(instruction.IP, ConvertOperand(instruction, 0), "dst = ZF==0 or SF==OF");
                break;
            case Mnemonic.Setge: 
                builder.CursedCPUFlags(instruction.IP, ConvertOperand(instruction, 0), "dst = SF==OF");
                break;
            case Mnemonic.Setbe: // dst = CF==1 or ZF==1
                builder.CursedCPUFlags(instruction.IP, ConvertOperand(instruction, 0), "dst = CF==1 or ZF==1");
                break;

            case Mnemonic.Setae: // dst = !CF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("CF"));
                builder.Xor(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;
            case Mnemonic.Setb: // dst = CF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("CF"));
                break;

            case Mnemonic.Seto: // dst = OF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("OF"));
                break;
            case Mnemonic.Setno: // dst = !OF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("OF"));
                builder.Xor(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Sets:// dst = SF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("SF"));
                break;
            case Mnemonic.Setns: // dst = !SF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("SF"));
                builder.Xor(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Sete: // dst = ZF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("ZF"));
                break;
            case Mnemonic.Setne: // dst = !ZF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("ZF"));
                builder.Xor(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Setp: // dst = PF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("PF"));
                break;
            case Mnemonic.Setnp: // dst = !PF
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeRegister("PF"));
                builder.Xor(instruction.IP, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Cmc: // CF = !CF
                builder.Xor(instruction.IP, InstructionSetIndependentOperand.MakeRegister("CF"), InstructionSetIndependentOperand.MakeImmediate(1));
                break;

            case Mnemonic.Clc: // CF = 0
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister("CF"), InstructionSetIndependentOperand.MakeImmediate(0));
                break;
            case Mnemonic.Cld: // DF = 0
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister("DF"), InstructionSetIndependentOperand.MakeImmediate(0));
                break;
            case Mnemonic.Cli: // IF = 0
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister("IF"), InstructionSetIndependentOperand.MakeImmediate(0));
                break;

            case Mnemonic.Bswap:
                builder.ByteSwap(instruction.IP, ConvertOperand(instruction, 0));
                break;

            // Subtracts the source from the destination, and subtracts 1 extra if the Carry Flag is set.
            case Mnemonic.Sbb: // But who needs those flags...
                builder.Subtract(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;

            // Shift Arithmetic Right. The Carry Flag contains the last bit shifted out.
            case Mnemonic.Sar: // Flags are a pain
                builder.ShiftRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Sal:
                builder.ShiftLeft(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;

            case Mnemonic.Sahf: // Transfers bits 0-7 of AH into the Flags Register.
                builder.Move(instruction.IP, InstructionSetIndependentOperand.MakeRegister("FLAGS"), InstructionSetIndependentOperand.MakeRegister("AH"));
                break;

            case Mnemonic.Rcr: // same but with CF maigc
            case Mnemonic.Ror:
                builder.RotateRight(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Rcl: // same but with CF magic
            case Mnemonic.Rol:
                builder.RotateLeft(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;

            case Mnemonic.Cmpxchg: // if dest == accum then dest = src
                builder.Compare(instruction.IP, ConvertOperand(instruction, 0), GetAccumulator(ConvertMemorySize(instruction.MemorySize)));
                builder.JumpIfEqual(instruction.IP, instruction.IP + 1);
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                builder.Nop(instruction.IP + 1);
                break;

            // very hard
            case Mnemonic.Cwd: // DX:AX := SignExtend(AX)
            case Mnemonic.Cdq: // EDX:EAX := SignExtend(EAX)
            case Mnemonic.Cqo: // RDX:RAX := SignExtend(RAX)
                goto default;

            //do these instructions even occur?
            case Mnemonic.Scasb: // Scan String  (Byte, Word or Doubleword) 
            case Mnemonic.Scasw: // Usage:  SCAS    string
            case Mnemonic.Scasd:
            case Mnemonic.Scasq:
            case Mnemonic.Stosb: // Store String  (Byte, Word or Doubleword)
            case Mnemonic.Stosw: // Usage:  STOS    dest
            case Mnemonic.Stosd:
            case Mnemonic.Stosq:
            case Mnemonic.Out: // port cursed things
            case Mnemonic.Outsb:
            case Mnemonic.Outsd:
            case Mnemonic.Outsw:

            default:
                builder.NotImplemented(instruction.IP, instruction.ToString());
                break;
        }
    }

    private InstructionSetIndependentOperand GetAccumulator(int size) => size switch
    {
        1 => InstructionSetIndependentOperand.MakeRegister("AL"),
        2 => InstructionSetIndependentOperand.MakeRegister("AX"),
        4 => InstructionSetIndependentOperand.MakeRegister("eax"),
        8 => InstructionSetIndependentOperand.MakeRegister("rax"),
        _ => throw new ArgumentException("bad size")
    };

    private static int ConvertMemorySize(MemorySize size)
    {
        switch (size)
        {
            case MemorySize.UInt8:
            case MemorySize.Int8:
                return 1;
            case MemorySize.Float16:
            case MemorySize.UInt16:
            case MemorySize.Int16:
                return 2;
            case MemorySize.UInt32:
            case MemorySize.Int32:
            case MemorySize.Float32:
                return 4;
            case MemorySize.Float64:
            case MemorySize.UInt64:
            case MemorySize.Int64:
                return 8;
            default:
                throw new NotImplementedException();
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
            return InstructionSetIndependentOperand.MakeStack((int) instruction.MemoryDisplacement32);

        //Memory
        //Most complex to least complex

        if (instruction.IsIPRelativeMemoryOperand)
            return InstructionSetIndependentOperand.MakeMemory(new(instruction.IPRelativeMemoryAddress));

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
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, instruction.MemoryDisplacement64));
        }

        //Only base
        if (instruction.MemoryBase != Register.None)
        {
            return InstructionSetIndependentOperand.MakeMemory(new(InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase))));
        }

        //Only addend
        return InstructionSetIndependentOperand.MakeMemory(new(instruction.MemoryDisplacement64));
    }
}
