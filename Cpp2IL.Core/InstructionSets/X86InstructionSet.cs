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

        foreach (var instruction in X86Utils.Iterate(context.RawBytes, context.UnderlyingPointer))
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
            case Mnemonic.Movzx: //For all intents and purposes we don't care about zero-extending
            case Mnemonic.Movaps: //Movaps is basically just a mov but with the potential future detail that the size is dependent on reg size
            case Mnemonic.Movups: //Movaps but unaligned
            case Mnemonic.Movss: //Same as movaps but for floats
            case Mnemonic.Movd: //Mov but specifically dword
            case Mnemonic.Movq: //Mov but specifically qword
            case Mnemonic.Movsd: //Mov but specifically double
            case Mnemonic.Movdqa: //Movaps but multiple integers at once in theory
            case Mnemonic.Cvtdq2ps: //Technically a convert double to single, but for analysis purposes we can just treat it as a move
                builder.Move(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
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
            case Mnemonic.Shl:
                builder.ShiftLeft(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Shr:
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
            case Mnemonic.Mulss:
            case Mnemonic.Vmulss:
                if (instruction.OpCount == 3)
                    builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                else if (instruction.OpCount == 2)
                    builder.Multiply(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                else
                    goto default;

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
            case Mnemonic.Pop:
                //var operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                //builder.ShiftStack(instruction.IP, operandSize);
                builder.Pop(instruction.IP, InstructionSetIndependentOperand.MakeRegister("rsp"), ConvertOperand(instruction, 0));
                break;
            case Mnemonic.Sub:
            case Mnemonic.Add:
                var isSubtract = instruction.Mnemonic == Mnemonic.Sub;

                //Special case - stack shift
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
                //Addss and subss are just floating point add/sub, but we don't need to handle the stack stuff
                //But we do need to handle 2 vs 3 operand forms
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
            case Mnemonic.Dec:
            case Mnemonic.Inc:
                // no CF
                var isDec = instruction.Mnemonic == Mnemonic.Dec;
                var im = InstructionSetIndependentOperand.MakeImmediate(1);
                if (isDec) builder.Subtract(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), im);
                else builder.Add(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), im);
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
                    });

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
                    var ptrSize = (int)context.AppContext.Binary.PointerSize;
                    registerParams = registerParams.Concat(Enumerable.Range(0, parameterCount).Select(p => p * ptrSize).Select(InstructionSetIndependentOperand.MakeStack)).ToList();

                    builder.Call(instruction.IP, target, registerParams.ToArray());

                    //Discard the consumed stack space
                    //builder.ShiftStack(instruction.IP, -parameterCount * 8);
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

                //Fall through to cmp, as test is just a cmp that doesn't set flags
                goto case Mnemonic.Cmp;
            case Mnemonic.Cmp:
            case Mnemonic.Comiss: //comiss is just a floating point compare
                builder.Compare(instruction.IP, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Jmp:
                if (instruction.Op0Kind != OpKind.Register)
                {
                    var jumpTarget = instruction.NearBranchTarget;

                    var methodEnd = instruction.IP + (ulong)context.RawBytes.Length;
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
                builder.Interrupt(instruction.IP); // We'll add it but eliminate later
                break;
            case Mnemonic.Nop:
                //While this is literally a nop and there's in theory no point emitting anything for it, it could be used as a jump target.
                //So we'll emit an ISIL nop for it.
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
}
