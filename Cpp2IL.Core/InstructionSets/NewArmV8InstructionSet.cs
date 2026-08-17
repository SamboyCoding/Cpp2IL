using System;
using System.Collections.Generic;
using System.Linq;
using Disarm;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Disarm.InternalDisassembly;

namespace Cpp2IL.Core.InstructionSets;

public class NewArmV8InstructionSet : Cpp2IlInstructionSet
{
    [ThreadStatic]
    private static Dictionary<string, ulong>? adrpOffsets;

    private static readonly Arm64CallingConventionResolver CallingConventions = new();

    public override BaseCallingConventionResolver CallingConventionResolver => CallingConventions;

    private static Immediate Imm(long value) => new(value);
    private static Immediate Imm(ulong value) => new(unchecked((long)value));

    private static string NormalizeRegister(Arm64Register reg) => reg switch
    {
        >= Arm64Register.W0 and <= Arm64Register.W31 => "X" + (reg - Arm64Register.W0),
        >= Arm64Register.X0 and <= Arm64Register.X31 => "X" + (reg - Arm64Register.X0),
        >= Arm64Register.V0 and <= Arm64Register.V31 => "V" + (reg - Arm64Register.V0),
        >= Arm64Register.D0 and <= Arm64Register.D31 => "V" + (reg - Arm64Register.D0),
        >= Arm64Register.S0 and <= Arm64Register.S31 => "V" + (reg - Arm64Register.S0),
        >= Arm64Register.H0 and <= Arm64Register.H31 => "V" + (reg - Arm64Register.H0),
        >= Arm64Register.B0 and <= Arm64Register.B31 => "V" + (reg - Arm64Register.B0),
        _ => reg.ToString()
    };

    private static Register Reg(Arm64Register reg) => new(null, NormalizeRegister(reg));

    // integer register 31 is SP or ZR depending on context, callers must decide which
    private static bool IsReg31(Arm64Register reg) => reg is Arm64Register.X31 or Arm64Register.W31;

    public override BinarySlice GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        var binary = context.AppContext.Binary;

        if (context is not ConcreteGenericMethodAnalysisContext)
        {
            //Managed method or attr gen => grab raw byte range between a and b
            var startOfNextFunction = MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer, binary);
            var count = (int)(startOfNextFunction - context.UnderlyingPointer);

            if (startOfNextFunction > 0)
            {
                var startRaw = (int)binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
                if (startRaw > 0 && startRaw + count <= binary.RawLength)
                    return new BinarySlice(binary, startRaw, count);
            }
        }

        var result = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(binary, context.UnderlyingPointer);
        var lastInsn = result.LastValid();

        var start = (int)binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
        // Map the last instruction (always within segment) and add 4 (ARM64 instruction size).
        // This avoids mapping endVa which may land exactly at a segment boundary gap.
        var end = (int)binary.MapVirtualAddressToRaw(lastInsn.Address) + 4;

        //Sanity check
        if (start < 0 || end < 0 || start >= binary.RawLength || end >= binary.RawLength)
            throw new Exception($"Failed to map virtual address 0x{context.UnderlyingPointer:X} to raw address for method {context!.DeclaringType?.FullName}/{context.Name} - start: 0x{start:X}, end: 0x{end:X} are out of bounds for length {binary.RawLength}.");

        return new BinarySlice(binary, start, end - start);
    }

    public override List<IOperand> GetParameterOperandsFromMethod(MethodAnalysisContext context)
    {
        return CallingConventions.ResolveForManaged(context).ToList();
    }

    public override (IReadOnlyList<ulong> DataReferences, IReadOnlyList<ulong> CallTargets) InspectPotentialThrowHelper(ApplicationAnalysisContext context, ulong address)
    {
        //Deliberately not calling NewArm64Utils here, it's too slow
        const int maxInstructions = 48;

        var binary = context.Binary;
        var rawStart = (int)binary.MapVirtualAddressToRaw(address);
        if (rawStart <= 0)
            return ([], []);

        var content = binary.GetRawBinaryContent();
        var window = System.Math.Min(maxInstructions * 4, content.Length - rawStart);
        if (window < 4)
            return ([], []);

        List<Arm64Instruction> body;
        try
        {
            body = Disassembler.Disassemble(content.Slice(rawStart, window), address, new Disassembler.Options(true, true, false)).ToList();
        }
        catch
        {
            return ([], []);
        }

        var dataReferences = new List<ulong>();
        var callTargets = new List<ulong>();
        var pages = new Dictionary<Arm64Register, ulong>();

        foreach (var insn in body)
        {
            switch (insn.Mnemonic)
            {
                case Arm64Mnemonic.ADRP:
                    pages[insn.Op0Reg] = (ulong)((long)(insn.Address & ~0xFFFUL) + insn.Op1Imm);
                    break;
                case Arm64Mnemonic.ADD when insn.Op2Kind == Arm64OperandKind.Immediate && pages.TryGetValue(insn.Op1Reg, out var page):
                    dataReferences.Add(page + (ulong)insn.Op2Imm);
                    break;
                case Arm64Mnemonic.ADR:
                    dataReferences.Add((ulong)((long)insn.Address + insn.Op1Imm));
                    break;
                case Arm64Mnemonic.BL:
                    callTargets.Add(insn.BranchTarget);
                    break;
            }

            if (insn.Mnemonic != Arm64Mnemonic.ADRP && insn.Op0Kind == Arm64OperandKind.Register)
                pages.Remove(insn.Op0Reg);
            
            if (insn.Mnemonic is Arm64Mnemonic.RET or Arm64Mnemonic.RETAA or Arm64Mnemonic.RETAB or Arm64Mnemonic.BR or Arm64Mnemonic.INVALID
                || (insn.Mnemonic == Arm64Mnemonic.B && insn.MnemonicConditionCode is Arm64ConditionCode.NONE or Arm64ConditionCode.AL))
                break;
        }

        return (dataReferences, callTargets);
    }

    public override List<Instruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var insns = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.AppContext.Binary, context.UnderlyingPointer);

        if (adrpOffsets == null) // initializers for ThreadStatic fields only run on the first thread
            adrpOffsets = new();
        else
            adrpOffsets.Clear();

        var instructions = new List<Instruction>();
        var addresses = new List<ulong>();

        foreach (var instruction in insns)
            ConvertInstructionStatement(instruction, instructions, addresses, context);

        // Add return if the function doesn't end with one already
        if (instructions.Count > 0 && instructions[^1].OpCode != OpCode.Return)
        {
            var index = instructions[^1].Index + 1;

            if (context.IsVoid)
                instructions.Add(new Instruction(index, OpCode.Return));
            else
                instructions.Add(new Instruction(index, OpCode.Return, CallingConventions.ReturnRegister(context)));
        }

        // fix branches
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            if (instruction.OpCode != OpCode.Jump && instruction.OpCode != OpCode.ConditionalJump)
                continue;

            var targetAddress = ((Immediate)instruction.Operands[0]).UnsignedValue;
            var targetIndex = addresses.FindIndex(addr => addr == targetAddress);

            if (targetIndex == -1)
            {
                instruction.OpCode = OpCode.Invalid;
                instruction.SetOperands(new StringLiteral($"Jump target not found in method: 0x{targetAddress:X4}"));
                continue;
            }

            var targetInstruction = instructions[targetIndex];

            instruction.SetOperand(0, targetInstruction);
        }

        adrpOffsets.Clear();
        return instructions;
    }

    private void ConvertInstructionStatement(Arm64Instruction instruction, List<Instruction> instructions, List<ulong> addresses, MethodAnalysisContext context)
    {
        var address = instruction.Address;

        Instruction Add(ulong address, OpCode opCode, params List<IOperand> operands)
        {
            addresses.Add(address);
            var newInstruction = new Instruction(instructions.Count, opCode, operands);
            instructions.Add(newInstruction);
            return newInstruction;
        }

        void AddCallAt(ulong target)
        {
            if (context.AppContext.MethodsByAddress.TryGetValue(target, out var possibleMethods) && possibleMethods.Count > 0)
            {
                MethodAnalysisContext ctx;
                if (possibleMethods.Count == 1)
                {
                    ctx = possibleMethods[0];
                }
                else
                {
                    // multiple methods folded onto one address, pick the one with the most arguments so nothing gets truncated
                    ctx = possibleMethods[0];
                    var mostArguments = -1;
                    foreach (var method in possibleMethods)
                    {
                        var arguments = method.Parameters.Count + (method.IsStatic ? 0 : 1);
                        if (arguments > mostArguments)
                        {
                            mostArguments = arguments;
                            ctx = method;
                        }
                    }
                }

                var call = ctx.IsVoid
                    ? Add(address, OpCode.CallVoid, Imm(target))
                    : Add(address, OpCode.Call, Imm(target), CallingConventions.ReturnRegister(ctx));

                call.AddOperands(CallingConventions.ResolveForManaged(ctx));
            }
            else
            {
                // Not a managed method, so we don't know its signature, preserve all argument registers
                var call = Add(address, OpCode.Call, Imm(target), new Register(null, "X0"));
                call.AddOperands(CallingConventions.ResolveForUnmanaged(context.AppContext, target));
            }
        }

        void AddReturn()
        {
            if (context.IsVoid)
                Add(address, OpCode.Return);
            else
                Add(address, OpCode.Return, CallingConventions.ReturnRegister(context));
        }

        // for pre/post indexed accesses, apply the base register update on the correct side of the access
        void EmitWriteback(bool beforeAccess)
        {
            var isPre = instruction.MemIndexMode == Arm64MemoryIndexMode.PreIndex;
            var isPost = instruction.MemIndexMode == Arm64MemoryIndexMode.PostIndex;

            if (!(beforeAccess ? isPre : isPost))
                return;

            if (IsReg31(instruction.MemBase))
                Add(address, OpCode.ShiftStack, Imm(instruction.MemOffset));
            else
                Add(address, OpCode.Add, Reg(instruction.MemBase), Reg(instruction.MemBase), Imm(instruction.MemOffset));
        }

        // the memory operand for the current instruction's access, offset by extraOffset (for the second reg of a pair)
        IOperand MemOperand(long extraOffset = 0)
        {
            var baseReg = instruction.MemBase;
            // writeback modes apply the offset to the base register itself, the access is at [base]
            var offset = (instruction.MemIndexMode == Arm64MemoryIndexMode.Offset ? instruction.MemOffset : 0) + extraOffset;

            if (baseReg == Arm64Register.INVALID)
                return new MemoryOperand(addend: offset);

            if (IsReg31(baseReg))
                return new StackOffset((int)offset);

            if (instruction.MemAddendReg != Arm64Register.INVALID)
                return new MemoryOperand(Reg(baseReg), Reg(instruction.MemAddendReg), offset, 1 << instruction.MemExtendOrShiftAmount);

            // a load through a register holding an ADRP page address is really an absolute load
            if (adrpOffsets!.TryGetValue(NormalizeRegister(baseReg), out var page))
                return new MemoryOperand(addend: (long)page + offset);

            return new MemoryOperand(Reg(baseReg), addend: offset);
        }

        var flagN = new Register(null, "N");
        var flagZ = new Register(null, "Z");
        var flagC = new Register(null, "C");
        var flagV = new Register(null, "V");

        // models op0 - op1, which CMP and friends are defined in terms of
        void EmitCompareFlags(IOperand op0, IOperand op1)
        {
            var temp1 = new Register(null, "TEMP1");
            var temp2 = new Register(null, "TEMP2");
            var temp3 = new Register(null, "TEMP3");
            var temp4 = new Register(null, "TEMP4");

            Add(address, OpCode.CheckLess, flagC, op0, op1); // arm's C is the inverse of a borrow
            Add(address, OpCode.Not, flagC, flagC);
            Add(address, OpCode.Subtract, temp1, op0, op1);
            Add(address, OpCode.CheckLess, flagN, temp1, Imm(0));
            Add(address, OpCode.CheckEqual, flagZ, temp1, Imm(0));
            Add(address, OpCode.Xor, temp2, op0, op1);
            Add(address, OpCode.Xor, temp3, op0, temp1);
            Add(address, OpCode.And, temp4, temp2, temp3);
            Add(address, OpCode.CheckLess, flagV, temp4, Imm(0));
        }

        void EmitResultFlags(IOperand result)
        {
            Add(address, OpCode.CheckLess, flagN, result, Imm(0));
            Add(address, OpCode.CheckEqual, flagZ, result, Imm(0));
            Add(address, OpCode.Move, flagC, Imm(0));
            Add(address, OpCode.Move, flagV, Imm(0));
        }

        // emits any instructions needed to evaluate the condition, returning an operand that is nonzero when it holds
        IOperand EmitCondition(Arm64ConditionCode condition)
        {
            var temp = new Register(null, "TEMPCOND");
            var temp2 = new Register(null, "TEMPCOND2");

            switch (condition)
            {
                case Arm64ConditionCode.EQ:
                    return flagZ;
                case Arm64ConditionCode.NE:
                    Add(address, OpCode.Not, temp, flagZ);
                    return temp;
                case Arm64ConditionCode.GE:
                    Add(address, OpCode.CheckEqual, temp, flagN, flagV);
                    return temp;
                case Arm64ConditionCode.LT:
                    Add(address, OpCode.CheckEqual, temp, flagN, flagV);
                    Add(address, OpCode.Not, temp, temp);
                    return temp;
                case Arm64ConditionCode.GT:
                    Add(address, OpCode.CheckEqual, temp, flagN, flagV);
                    Add(address, OpCode.Not, temp2, flagZ);
                    Add(address, OpCode.And, temp, temp, temp2);
                    return temp;
                case Arm64ConditionCode.LE:
                    Add(address, OpCode.CheckEqual, temp, flagN, flagV);
                    Add(address, OpCode.Not, temp, temp);
                    Add(address, OpCode.Or, temp, temp, flagZ);
                    return temp;
                case Arm64ConditionCode.CS: // unsigned >=
                    return flagC;
                case Arm64ConditionCode.CC: // unsigned <
                    Add(address, OpCode.Not, temp, flagC);
                    return temp;
                case Arm64ConditionCode.HI: // unsigned >
                    Add(address, OpCode.Not, temp, flagZ);
                    Add(address, OpCode.And, temp, flagC, temp);
                    return temp;
                case Arm64ConditionCode.LS: // unsigned <=
                    Add(address, OpCode.Not, temp, flagC);
                    Add(address, OpCode.Or, temp, temp, flagZ);
                    return temp;
                case Arm64ConditionCode.MI:
                    return flagN;
                case Arm64ConditionCode.PL:
                    Add(address, OpCode.Not, temp, flagN);
                    return temp;
                case Arm64ConditionCode.VS:
                    return flagV;
                case Arm64ConditionCode.VC:
                    Add(address, OpCode.Not, temp, flagV);
                    return temp;
                default: // AL/NV are both unconditional
                    return Imm(1);
            }
        }

        // dest = cond ? <emitTrueValue into dest> : <emitFalseValue into dest>
        void EmitConditionalAssign(Arm64ConditionCode condition, Action emitTrueValue, Action<ulong> emitFalseValue)
        {
            var inverse = new Register(null, "TEMPCSEL");
            Add(address, OpCode.Not, inverse, EmitCondition(condition));
            Add(address, OpCode.ConditionalJump, Imm(address + 1), inverse);
            emitTrueValue();
            Add(address, OpCode.Jump, Imm(address + 2));
            emitFalseValue(address + 1);
            Add(address + 2, OpCode.Nop);
        }

        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.MOV:
            case Arm64Mnemonic.MOVZ:
            case Arm64Mnemonic.FMOV:
            case Arm64Mnemonic.SXTB:
            case Arm64Mnemonic.SXTH:
            case Arm64Mnemonic.SXTW:
            case Arm64Mnemonic.UXTB:
            case Arm64Mnemonic.UXTH:
            // conversions are moves for analysis purposes, same as the x86 handling of cvt*
            case Arm64Mnemonic.FCVT:
            case Arm64Mnemonic.FCVTZS:
            case Arm64Mnemonic.FCVTZU:
            case Arm64Mnemonic.FCVTMS:
            case Arm64Mnemonic.FCVTMU:
            case Arm64Mnemonic.FCVTNS:
            case Arm64Mnemonic.FCVTNU:
            case Arm64Mnemonic.FCVTPS:
            case Arm64Mnemonic.FCVTPU:
            case Arm64Mnemonic.FCVTAS:
            case Arm64Mnemonic.FCVTAU:
            case Arm64Mnemonic.SCVTF:
            case Arm64Mnemonic.UCVTF:
                if (instruction.Op0Kind == Arm64OperandKind.Register && IsReg31(instruction.Op0Reg))
                {
                    Add(address, OpCode.Nop); // write to xzr, discard
                    break;
                }

                Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Arm64Mnemonic.MOVI:
            case Arm64Mnemonic.MVNI when instruction.Op1Kind == Arm64OperandKind.Immediate:
                {
                    var value = instruction.Mnemonic == Arm64Mnemonic.MVNI ? ~instruction.Op1Imm : instruction.Op1Imm;
                    Add(address, OpCode.Move, ConvertOperand(instruction, 0), Imm(value));
                    break;
                }
            case Arm64Mnemonic.MOVK:
                // inserts a 16-bit chunk, which after the movz that always precedes it is just an or
                Add(address, OpCode.Or, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0), Imm(instruction.Op1Imm));
                break;
            case Arm64Mnemonic.MOVN:
                {
                    var temp = new Register(null, "TEMP");
                    Add(address, OpCode.Move, temp, ConvertOperand(instruction, 1));
                    Add(address, OpCode.Not, temp, temp);
                    Add(address, OpCode.Move, ConvertOperand(instruction, 0), temp);
                    break;
                }
            case Arm64Mnemonic.MVN:
                Add(address, OpCode.Not, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Arm64Mnemonic.ADR:
                Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Arm64Mnemonic.ADRP:
                {
                    var target = (long)(address & ~0xFFFUL) + instruction.Op1Imm;
                    Add(address, OpCode.Move, ConvertOperand(instruction, 0), Imm(target));
                    adrpOffsets![NormalizeRegister(instruction.Op0Reg)] = (ulong)target;
                    break;
                }
            case Arm64Mnemonic.LDR:
            case Arm64Mnemonic.LDRB:
            case Arm64Mnemonic.LDRH:
            case Arm64Mnemonic.LDRSB:
            case Arm64Mnemonic.LDRSH:
            case Arm64Mnemonic.LDRSW:
            case Arm64Mnemonic.LDUR:
            case Arm64Mnemonic.LDURB:
            case Arm64Mnemonic.LDURH:
            case Arm64Mnemonic.LDURSB:
            case Arm64Mnemonic.LDURSH:
            case Arm64Mnemonic.LDURSW:
                {
                    EmitWriteback(beforeAccess: true);

                    // ldr with a pc-relative literal is an absolute load
                    var source = instruction.Op1Kind == Arm64OperandKind.ImmediatePcRelative
                        ? new MemoryOperand(addend: (long)address + instruction.Op1Imm)
                        : MemOperand();

                    if (instruction.Op0Kind == Arm64OperandKind.Register && IsReg31(instruction.Op0Reg))
                        Add(address, OpCode.Nop); // load to xzr = prefetch, discard
                    else
                        Add(address, OpCode.Move, ConvertOperand(instruction, 0), source);

                    EmitWriteback(beforeAccess: false);
                    break;
                }
            case Arm64Mnemonic.STR:
            case Arm64Mnemonic.STRB:
            case Arm64Mnemonic.STRH:
            case Arm64Mnemonic.STUR:
            case Arm64Mnemonic.STURB:
            case Arm64Mnemonic.STURH:
                EmitWriteback(beforeAccess: true);
                Add(address, OpCode.Move, MemOperand(), ConvertOperand(instruction, 0));
                EmitWriteback(beforeAccess: false);
                break;
            case Arm64Mnemonic.LDP:
            case Arm64Mnemonic.LDPSW:
            case Arm64Mnemonic.STP:
                {
                    var pairSize = instruction.Op0Reg switch
                    {
                        >= Arm64Register.V0 and <= Arm64Register.V31 => 16,
                        >= Arm64Register.D0 and <= Arm64Register.D31 => 8,
                        >= Arm64Register.S0 and <= Arm64Register.S31 => 4,
                        >= Arm64Register.W0 and <= Arm64Register.W31 => 4,
                        _ => 8
                    };

                    EmitWriteback(beforeAccess: true);

                    if (instruction.Mnemonic == Arm64Mnemonic.STP)
                    {
                        Add(address, OpCode.Move, MemOperand(), ConvertOperand(instruction, 0));
                        Add(address, OpCode.Move, MemOperand(pairSize), ConvertOperand(instruction, 1));
                    }
                    else
                    {
                        Add(address, OpCode.Move, ConvertOperand(instruction, 0), MemOperand());
                        Add(address, OpCode.Move, ConvertOperand(instruction, 1), MemOperand(pairSize));
                    }

                    EmitWriteback(beforeAccess: false);
                    break;
                }
            case Arm64Mnemonic.ADD:
            case Arm64Mnemonic.SUB:
            case Arm64Mnemonic.ADDS:
            case Arm64Mnemonic.SUBS:
                {
                    var isSubtract = instruction.Mnemonic is Arm64Mnemonic.SUB or Arm64Mnemonic.SUBS;
                    var setsFlags = instruction.Mnemonic is Arm64Mnemonic.ADDS or Arm64Mnemonic.SUBS;

                    // stack pointer adjustment
                    if (IsReg31(instruction.Op0Reg) && IsReg31(instruction.Op1Reg) && instruction.Op2Kind == Arm64OperandKind.Immediate && !setsFlags)
                    {
                        Add(address, OpCode.ShiftStack, Imm(isSubtract ? -instruction.Op2Imm : instruction.Op2Imm));
                        break;
                    }

                    // in the immediate forms register 31 is sp, so this takes the address of a stack slot
                    if (IsReg31(instruction.Op1Reg) && instruction.Op2Kind == Arm64OperandKind.Immediate && !IsReg31(instruction.Op0Reg))
                    {
                        var slot = new StackOffset((int)(isSubtract ? -instruction.Op2Imm : instruction.Op2Imm));
                        Add(address, OpCode.Move, ConvertOperand(instruction, 0), new AddressOf(slot));
                        break;
                    }

                    var src1 = ConvertOperand(instruction, 1);
                    var src2 = ConvertOperand(instruction, 2);
                    // a discarded result means this is only about the flags
                    var dest = IsReg31(instruction.Op0Reg) ? new Register(null, "TEMP") : ConvertOperand(instruction, 0);

                    Add(address, isSubtract ? OpCode.Subtract : OpCode.Add, dest, src1, src2);

                    if (setsFlags)
                    {
                        if (isSubtract)
                            EmitCompareFlags(src1, src2);
                        else
                            EmitResultFlags(dest);
                    }

                    break;
                }
            case Arm64Mnemonic.CMP:
            case Arm64Mnemonic.FCMP:
            case Arm64Mnemonic.FCMPE:
                EmitCompareFlags(ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Arm64Mnemonic.CMN:
                // cmp against the negated operand
                if (instruction.Op1Kind == Arm64OperandKind.Immediate)
                {
                    EmitCompareFlags(ConvertOperand(instruction, 0), Imm(-instruction.Op1Imm));
                }
                else
                {
                    var negated = new Register(null, "TEMP");
                    Add(address, OpCode.Negate, negated, ConvertOperand(instruction, 1));
                    EmitCompareFlags(ConvertOperand(instruction, 0), negated);
                }

                break;
            case Arm64Mnemonic.TST:
                {
                    var temp = new Register(null, "TEMP");
                    Add(address, OpCode.And, temp, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                    EmitResultFlags(temp);
                    break;
                }
            case Arm64Mnemonic.AND:
            case Arm64Mnemonic.ANDS:
            case Arm64Mnemonic.ORR:
            case Arm64Mnemonic.EOR:
                {
                    var opCode = instruction.Mnemonic switch
                    {
                        Arm64Mnemonic.ORR => OpCode.Or,
                        Arm64Mnemonic.EOR => OpCode.Xor,
                        _ => OpCode.And
                    };

                    var dest = IsReg31(instruction.Op0Reg) ? new Register(null, "TEMP") : ConvertOperand(instruction, 0);
                    Add(address, opCode, dest, ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));

                    if (instruction.Mnemonic == Arm64Mnemonic.ANDS)
                        EmitResultFlags(dest);

                    break;
                }
            case Arm64Mnemonic.BIC:
            case Arm64Mnemonic.BICS:
            case Arm64Mnemonic.ORN:
            case Arm64Mnemonic.EON:
                {
                    var temp = new Register(null, "TEMP");
                    Add(address, OpCode.Not, temp, ConvertOperand(instruction, 2));
                    var opCode = instruction.Mnemonic switch
                    {
                        Arm64Mnemonic.ORN => OpCode.Or,
                        Arm64Mnemonic.EON => OpCode.Xor,
                        _ => OpCode.And
                    };
                    var dest = IsReg31(instruction.Op0Reg) ? new Register(null, "TEMP") : ConvertOperand(instruction, 0);
                    Add(address, opCode, dest, ConvertOperand(instruction, 1), temp);

                    if (instruction.Mnemonic == Arm64Mnemonic.BICS)
                        EmitResultFlags(dest);

                    break;
                }
            case Arm64Mnemonic.CCMP:
            case Arm64Mnemonic.CCMN:
            case Arm64Mnemonic.FCCMP:
            case Arm64Mnemonic.FCCMPE:
                {
                    // if cond holds the flags come from the comparison, else straight from the nzcv immediate
                    var inverse = new Register(null, "TEMPCSEL");
                    Add(address, OpCode.Not, inverse, EmitCondition(instruction.FinalOpConditionCode));
                    Add(address, OpCode.ConditionalJump, Imm(address + 1), inverse);

                    var op1 = ConvertOperand(instruction, 1);
                    if (instruction.Mnemonic == Arm64Mnemonic.CCMN)
                    {
                        var negated = new Register(null, "TEMP");
                        Add(address, OpCode.Negate, negated, op1);
                        op1 = negated;
                    }

                    EmitCompareFlags(ConvertOperand(instruction, 0), op1);
                    Add(address, OpCode.Jump, Imm(address + 2));

                    var nzcv = instruction.Op2Imm;
                    Add(address + 1, OpCode.Move, flagN, Imm((nzcv >> 3) & 1));
                    Add(address + 1, OpCode.Move, flagZ, Imm((nzcv >> 2) & 1));
                    Add(address + 1, OpCode.Move, flagC, Imm((nzcv >> 1) & 1));
                    Add(address + 1, OpCode.Move, flagV, Imm(nzcv & 1));

                    Add(address + 2, OpCode.Nop);
                    break;
                }
            case Arm64Mnemonic.NEG:
            case Arm64Mnemonic.FNEG:
                Add(address, OpCode.Negate, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Arm64Mnemonic.LSL:
                Add(address, OpCode.ShiftLeft, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Arm64Mnemonic.LSR:
            case Arm64Mnemonic.ASR:
                Add(address, OpCode.ShiftRight, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Arm64Mnemonic.UBFX:
            case Arm64Mnemonic.SBFX:
                {
                    // dest = (src >> lsb) & ((1 << width) - 1)
                    var dest = ConvertOperand(instruction, 0);
                    Add(address, OpCode.ShiftRight, dest, ConvertOperand(instruction, 1), Imm(instruction.Op2Imm));
                    Add(address, OpCode.And, dest, dest, Imm((1L << (int)instruction.Op3Imm) - 1));
                    break;
                }
            case Arm64Mnemonic.UBFIZ:
            case Arm64Mnemonic.SBFIZ:
                {
                    // dest = (src & ((1 << width) - 1)) << shift
                    var dest = ConvertOperand(instruction, 0);
                    var temp = new Register(null, "TEMP");
                    Add(address, OpCode.And, temp, ConvertOperand(instruction, 1), Imm((1L << (int)instruction.Op3Imm) - 1));
                    Add(address, OpCode.ShiftLeft, dest, temp, Imm(instruction.Op2Imm));
                    break;
                }
            case Arm64Mnemonic.MUL:
            case Arm64Mnemonic.FMUL:
            case Arm64Mnemonic.SMULL:
            case Arm64Mnemonic.UMULL:
                Add(address, OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Arm64Mnemonic.MNEG:
            case Arm64Mnemonic.SMNEGL:
            case Arm64Mnemonic.UMNEGL:
            case Arm64Mnemonic.FNMUL:
                {
                    var dest = ConvertOperand(instruction, 0);
                    Add(address, OpCode.Multiply, dest, ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                    Add(address, OpCode.Negate, dest, dest);
                    break;
                }
            case Arm64Mnemonic.MADD:
            case Arm64Mnemonic.MSUB:
            case Arm64Mnemonic.SMADDL:
            case Arm64Mnemonic.UMADDL:
            case Arm64Mnemonic.SMSUBL:
            case Arm64Mnemonic.UMSUBL:
                {
                    // rd = ra +/- rn * rm
                    var isAdd = instruction.Mnemonic is Arm64Mnemonic.MADD or Arm64Mnemonic.SMADDL or Arm64Mnemonic.UMADDL;
                    var temp = new Register(null, "TEMP");
                    Add(address, OpCode.Multiply, temp, ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                    Add(address, isAdd ? OpCode.Add : OpCode.Subtract, ConvertOperand(instruction, 0), ConvertOperand(instruction, 3), temp);
                    break;
                }
            case Arm64Mnemonic.EXTR:
                {
                    // dest = (rn:rm) >> lsb
                    var dest = ConvertOperand(instruction, 0);
                    var lsb = instruction.Op3Imm;

                    if (lsb == 0)
                    {
                        Add(address, OpCode.Move, dest, ConvertOperand(instruction, 2));
                        break;
                    }

                    var regSize = instruction.Op0Reg is >= Arm64Register.X0 and <= Arm64Register.X31 ? 64 : 32;
                    var temp = new Register(null, "TEMP");
                    var temp2 = new Register(null, "TEMP2");
                    Add(address, OpCode.ShiftRight, temp, ConvertOperand(instruction, 2), Imm(lsb));
                    Add(address, OpCode.ShiftLeft, temp2, ConvertOperand(instruction, 1), Imm(regSize - lsb));
                    Add(address, OpCode.Or, dest, temp, temp2);
                    break;
                }
            case Arm64Mnemonic.SDIV:
            case Arm64Mnemonic.UDIV:
            case Arm64Mnemonic.FDIV:
                Add(address, OpCode.Divide, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Arm64Mnemonic.FADD:
                Add(address, OpCode.Add, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Arm64Mnemonic.FSUB:
                Add(address, OpCode.Subtract, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;
            case Arm64Mnemonic.BL:
                AddCallAt(instruction.BranchTarget);
                break;
            case Arm64Mnemonic.BLR:
                {
                    var call = Add(address, OpCode.IndirectCall, ConvertOperand(instruction, 0), new Register(null, "X0") /* return value */);
                    call.AddOperands(CallingConventions.ResolveForUnmanaged(context.AppContext, address));
                    break;
                }
            case Arm64Mnemonic.BR:
                {
                    // tail call or jump table, either way it leaves the method
                    var jump = Add(address, OpCode.IndirectJump, ConvertOperand(instruction, 0), new Register(null, "X0") /* return value */);
                    jump.AddOperands(CallingConventions.ResolveForUnmanaged(context.AppContext, address));
                    break;
                }
            case Arm64Mnemonic.B:
            case Arm64Mnemonic.BC:
                {
                    var target = instruction.Mnemonic == Arm64Mnemonic.B ? instruction.BranchTarget : instruction.Op0PcRelImm;

                    if (instruction.MnemonicConditionCode != Arm64ConditionCode.NONE
                        && instruction.MnemonicConditionCode != Arm64ConditionCode.AL
                        && instruction.MnemonicConditionCode != Arm64ConditionCode.NV)
                    {
                        Add(address, OpCode.ConditionalJump, Imm(target), EmitCondition(instruction.MnemonicConditionCode));
                        break;
                    }

                    if (target < context.UnderlyingPointer || target >= context.UnderlyingPointer + (ulong)context.RawBytes.Length)
                    {
                        // unconditional branch out of the method is a tail call
                        AddCallAt(target);
                        AddReturn();
                    }
                    else
                    {
                        Add(address, OpCode.Jump, Imm(target));
                    }

                    break;
                }
            case Arm64Mnemonic.RET:
            case Arm64Mnemonic.RETAA:
            case Arm64Mnemonic.RETAB:
                AddReturn();
                break;
            case Arm64Mnemonic.CBZ:
            case Arm64Mnemonic.CBNZ:
                {
                    var target = (ulong)((long)address + instruction.Op1Imm);
                    var temp = new Register(null, "TEMP");

                    Add(address, OpCode.CheckEqual, temp, ConvertOperand(instruction, 0), Imm(0));

                    if (instruction.Mnemonic == Arm64Mnemonic.CBNZ)
                        Add(address, OpCode.Not, temp, temp);

                    Add(address, OpCode.ConditionalJump, Imm(target), temp);
                    break;
                }
            case Arm64Mnemonic.TBZ:
            case Arm64Mnemonic.TBNZ:
                {
                    var target = (ulong)((long)address + instruction.Op2Imm);
                    var temp = new Register(null, "TEMP");

                    Add(address, OpCode.And, temp, ConvertOperand(instruction, 0), Imm(1L << (int)instruction.Op1Imm));
                    Add(address, OpCode.CheckEqual, temp, temp, Imm(0));

                    if (instruction.Mnemonic == Arm64Mnemonic.TBNZ)
                        Add(address, OpCode.Not, temp, temp);

                    Add(address, OpCode.ConditionalJump, Imm(target), temp);
                    break;
                }
            case Arm64Mnemonic.CSEL:
            case Arm64Mnemonic.FCSEL:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)),
                    falseAddress => Add(falseAddress, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 2)));
                break;
            case Arm64Mnemonic.CSINC:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)),
                    falseAddress => Add(falseAddress, OpCode.Add, ConvertOperand(instruction, 0), ConvertOperand(instruction, 2), Imm(1)));
                break;
            case Arm64Mnemonic.CSINV:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)),
                    falseAddress => Add(falseAddress, OpCode.Not, ConvertOperand(instruction, 0), ConvertOperand(instruction, 2)));
                break;
            case Arm64Mnemonic.CSNEG:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)),
                    falseAddress => Add(falseAddress, OpCode.Negate, ConvertOperand(instruction, 0), ConvertOperand(instruction, 2)));
                break;
            case Arm64Mnemonic.CSET:
                Add(address, OpCode.Move, ConvertOperand(instruction, 0), EmitCondition(instruction.FinalOpConditionCode));
                break;
            case Arm64Mnemonic.CSETM:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Move, ConvertOperand(instruction, 0), Imm(-1L)),
                    falseAddress => Add(falseAddress, OpCode.Move, ConvertOperand(instruction, 0), Imm(0)));
                break;
            case Arm64Mnemonic.CINC:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Add, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), Imm(1)),
                    falseAddress => Add(falseAddress, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)));
                break;
            case Arm64Mnemonic.CNEG:
                EmitConditionalAssign(instruction.FinalOpConditionCode,
                    () => Add(address, OpCode.Negate, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)),
                    falseAddress => Add(falseAddress, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1)));
                break;
            case Arm64Mnemonic.NOP:
            // pointer auth and branch target hints are meaningless for analysis but might be jump targets
            case Arm64Mnemonic.BTI:
            case Arm64Mnemonic.BTI_C:
            case Arm64Mnemonic.BTI_J:
            case Arm64Mnemonic.BTI_JC:
            case Arm64Mnemonic.PACIASP:
            case Arm64Mnemonic.PACIBSP:
            case Arm64Mnemonic.AUTIASP:
            case Arm64Mnemonic.AUTIBSP:
                Add(address, OpCode.Nop);
                break;
            case Arm64Mnemonic.BRK:
            case Arm64Mnemonic.UDF:
                Add(address, OpCode.Interrupt);
                break;
            case Arm64Mnemonic.MRS:
                // system register read (thread pointer etc), value is opaque to analysis
                Add(address, OpCode.Move, ConvertOperand(instruction, 0), new Register(null, "SYSREG"));
                break;
            default:
                Add(address, OpCode.NotImplemented, new StringLiteral($"Instruction {instruction.Mnemonic} not yet implemented."));
                break;
        }

        // any register write invalidates a tracked ADRP page address (ADRP itself just set one)
        if (instruction.Mnemonic != Arm64Mnemonic.ADRP && instruction.Op0Kind == Arm64OperandKind.Register)
            adrpOffsets!.Remove(NormalizeRegister(instruction.Op0Reg));
        if (instruction.MemIndexMode != Arm64MemoryIndexMode.Offset && instruction.MemBase != Arm64Register.INVALID)
            adrpOffsets!.Remove(NormalizeRegister(instruction.MemBase));
    }

    private IOperand ConvertOperand(Arm64Instruction instruction, int operand)
    {
        var kind = operand switch
        {
            0 => instruction.Op0Kind,
            1 => instruction.Op1Kind,
            2 => instruction.Op2Kind,
            3 => instruction.Op3Kind,
            _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
        };

        if (kind is Arm64OperandKind.Immediate or Arm64OperandKind.ImmediatePcRelative)
        {
            var imm = operand switch
            {
                0 => instruction.Op0Imm,
                1 => instruction.Op1Imm,
                2 => instruction.Op2Imm,
                3 => instruction.Op3Imm,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            if (kind == Arm64OperandKind.ImmediatePcRelative)
                imm += (long)instruction.Address;

            return new Immediate(imm);
        }

        if (kind == Arm64OperandKind.FloatingPointImmediate)
        {
            var imm = operand switch
            {
                0 => instruction.Op0FpImm,
                1 => instruction.Op1FpImm,
                2 => instruction.Op2FpImm,
                3 => instruction.Op3FpImm,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            return new DoubleLiteral(imm);
        }

        if (kind == Arm64OperandKind.Register)
        {
            var reg = operand switch
            {
                0 => instruction.Op0Reg,
                1 => instruction.Op1Reg,
                2 => instruction.Op2Reg,
                3 => instruction.Op3Reg,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            // reads of integer register 31 are the zero register (sp-based reads are special-cased by callers)
            if (IsReg31(reg))
                return new Immediate(0);

            return Reg(reg);
        }

        if (kind == Arm64OperandKind.Memory)
        {
            var reg = instruction.MemBase;
            var offset = instruction.MemOffset;

            if (reg == Arm64Register.INVALID)
                //Offset only
                return new MemoryOperand(addend: offset);

            if (IsReg31(reg))
                return new StackOffset((int)offset);

            return new MemoryOperand(Reg(reg), addend: offset);
        }

        if (kind == Arm64OperandKind.VectorRegisterElement)
        {
            var reg = operand switch
            {
                0 => instruction.Op0Reg,
                1 => instruction.Op1Reg,
                2 => instruction.Op2Reg,
                3 => instruction.Op3Reg,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            var vectorElement = operand switch
            {
                0 => instruction.Op0VectorElement,
                1 => instruction.Op1VectorElement,
                2 => instruction.Op2VectorElement,
                3 => instruction.Op3VectorElement,
                _ => throw new ArgumentOutOfRangeException(nameof(operand), $"Operand must be between 0 and 3, inclusive. Got {operand}")
            };

            var width = vectorElement.Width switch
            {
                Arm64VectorElementWidth.B => "B",
                Arm64VectorElementWidth.H => "H",
                Arm64VectorElementWidth.S => "S",
                Arm64VectorElementWidth.D => "D",
                _ => throw new ArgumentOutOfRangeException(nameof(vectorElement.Width), $"Unknown vector element width {vectorElement.Width}")
            };

            var name = $"{NormalizeRegister(reg)}.{width}{vectorElement.Index}";
            return new Register(null, name);
        }

        return new StringLiteral($"<UNIMPLEMENTED OPERAND TYPE {kind}>");
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new NewArm64KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context) => context.RawBytes.Length <= 0 ? "" : string.Join("\n", Disassembler.Disassemble(context.RawBytes.AsSpan(), context.UnderlyingPointer, new Disassembler.Options(true, true, false)).ToList());
}
