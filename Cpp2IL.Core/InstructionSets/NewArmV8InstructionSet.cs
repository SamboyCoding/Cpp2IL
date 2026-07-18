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
    private static Dictionary<Arm64Register, ulong> adrpOffsets = new();

    public override BinarySlice GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        var binary = context.AppContext.Binary;

        if (context is not ConcreteGenericMethodAnalysisContext)
        {
            //Managed method or attr gen => grab raw byte range between a and b
            var startOfNextFunction = (int)MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer, binary);
            var ptrAsInt = (int)context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return new BinarySlice(binary, ptrAsInt, count);
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

    public override List<object> GetParameterOperandsFromMethod(MethodAnalysisContext context)
    {
        // Is this correct (?)
        return GetArgumentOperandsForCall(context);
    }

    public override List<Instruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var insns = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.AppContext.Binary, context.UnderlyingPointer);

        if (adrpOffsets == null!) // initializers for ThreadStatic fields only run on the first thread
            adrpOffsets = new();
        else
            adrpOffsets.Clear();

        var instructions = new List<Instruction>();
        var addresses = new List<ulong>();

        foreach (var instruction in insns)
            ConvertInstructionStatement(instruction, instructions, addresses, context);

        // fix branches
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            if (instruction.OpCode != OpCode.Jump && instruction.OpCode != OpCode.ConditionalJump)
                continue;

            var targetAddress = (ulong)instruction.Operands[0];
            var targetIndex = addresses.FindIndex(addr => addr == targetAddress);

            if (targetIndex == -1)
            {
                instruction.OpCode = OpCode.Invalid;
                instruction.Operands = [$"Jump target not found in method: 0x{targetAddress:X4}"];
                continue;
            }

            var targetInstruction = instructions[targetIndex];

            instruction.Operands[0] = targetInstruction;
        }

        adrpOffsets.Clear();
        return instructions;
    }

    private void ConvertInstructionStatement(Arm64Instruction instruction, List<Instruction> instructions, List<ulong> addresses, MethodAnalysisContext context)
    {
        var address = instruction.Address;

        Instruction Add(ulong address, OpCode opCode, params object[] operands)
        {
            addresses.Add(address);
            var newInstruction = new Instruction(instructions.Count, opCode, operands);
            instructions.Add(newInstruction);
            return newInstruction;
        }
        
        void AddCall(MethodAnalysisContext context, object? returnRegister2, ulong address, ulong target)
        {
            var call = returnRegister2 == null ? 
                Add(address, OpCode.CallVoid, target) : 
                Add(address, OpCode.Call, target, returnRegister2);

            call.Operands.AddRange(GetArgumentOperandsForCall(context, target));
        }

        switch (instruction.Mnemonic)
        {
            case Arm64Mnemonic.MOV:
            case Arm64Mnemonic.MOVZ:
            case Arm64Mnemonic.FMOV:
            case Arm64Mnemonic.SXTW: // move and sign extend Wn to Xd
            case Arm64Mnemonic.LDR:
            case Arm64Mnemonic.LDRB:
                //Load and move are (dest, src)

                if (instruction.MemIsPreIndexed) //  such as  X8, [X19,#0x30]! 
                {
                    //Regardless of anything else, we're trashing any possible ADRP offsets in the dest here, so let's clear that
                    if (instruction.Op0Kind == Arm64OperandKind.Register)
                        adrpOffsets.Remove(instruction.Op0Reg);

                    var operate = ConvertOperand(instruction, 1);
                    if (operate is MemoryOperand operand)
                    {
                        var register = (Register)operand.Base!;
                        // X19= X19, #0x30
                        Add(address, OpCode.Add, register, register, operand.Addend);
                        //X8 = [X19]
                        Add(address, OpCode.Move, ConvertOperand(instruction, 0), new MemoryOperand(new Register(null, register.ToString()!.ToUpperInvariant())));
                        break;
                    }
                }

                if (instruction.Op1Kind == Arm64OperandKind.Memory && adrpOffsets.TryGetValue(instruction.MemBase, out var page) && instruction.MemOffset != 0 && instruction.MemAddendReg == Arm64Register.INVALID)
                {
                    //Maybe this is a bit hacky? But I really don't want to write paged load handling into ISIL itself, it's an Arm64 quirk
                    //LDR X0, [X1, #0x1000], where X1 was previously loaded with a page address via an ADRP instruction
                    //We just return the final address, it makes ISIL happier.
                    //TODO check if this is correct
                    var offset = instruction.MemOffset + (long)page;

                    //We're also trashing any possible ADRP offsets in the dest here, so let's clear that now we've possibly grabbed the value if we need it (it's common to store the page and final address in the same register)
                    if (instruction.Op0Kind == Arm64OperandKind.Register)
                        adrpOffsets.Remove(instruction.Op0Reg);

                    Add(address, OpCode.Move, ConvertOperand(instruction, 0), new MemoryOperand(addend: offset));
                    break;
                }

                //And again here we're trashing any possible ADRP offsets in the dest here, so let's clear that
                if (instruction.Op0Kind == Arm64OperandKind.Register)
                    adrpOffsets.Remove(instruction.Op0Reg);

                Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                Add(address, OpCode.CheckEqual, new Register(null, "Z"), ConvertOperand(instruction, 0), 0);
                break;
            case Arm64Mnemonic.MOVN:
                {
                    // dest = ~src

                    //See above re: ADRP offsets
                    if (instruction.Op0Kind == Arm64OperandKind.Register)
                        adrpOffsets.Remove(instruction.Op0Reg);

                    var temp2 = new Register(null, "TEMP");
                    Add(address, OpCode.Move, temp2, ConvertOperand(instruction, 1));
                    Add(address, OpCode.Not, temp2, temp2);
                    Add(address, OpCode.Move, ConvertOperand(instruction, 0), temp2);
                    break;
                }
            case Arm64Mnemonic.STR:
            case Arm64Mnemonic.STUR: // unscaled
            case Arm64Mnemonic.STRB:
                //Store is (src, dest)
                Add(address, OpCode.Move, ConvertOperand(instruction, 1), ConvertOperand(instruction, 0));
                break;
            case Arm64Mnemonic.STP:
                // store pair of registers (reg1, reg2, dest)
                {
                    var dest3 = ConvertOperand(instruction, 2);
                    if (dest3 is Register { Name: "X31" }) // if stack
                    {
                        Add(address, OpCode.Move, dest3, ConvertOperand(instruction, 0));
                        Add(address, OpCode.Move, dest3, ConvertOperand(instruction, 1));
                    }
                    else if (dest3 is MemoryOperand memory)
                    {
                        var firstRegister = ConvertOperand(instruction, 0);
                        long size = ((Register)firstRegister).Name[0] == 'W' ? 4 : 8;
                        Add(address, OpCode.Move, dest3, firstRegister); // [REG + offset] = REG1
                        memory = new MemoryOperand((Register)memory.Base!, addend: memory.Addend + size);
                        dest3 = memory;
                        Add(address, OpCode.Move, dest3, ConvertOperand(instruction, 1)); // [REG + offset + size] = REG2
                    }
                    else // reg pointer
                    {
                        var firstRegister = ConvertOperand(instruction, 0);
                        long size = ((Register)firstRegister).Name[0] == 'W' ? 4 : 8;
                        Add(address, OpCode.Move, dest3, firstRegister);
                        Add(address, OpCode.Add, dest3, dest3, size);
                        Add(address, OpCode.Move, dest3, ConvertOperand(instruction, 1));
                    }
                }
                break;
            case Arm64Mnemonic.ADRP:
                //Just handle as a move
                Add(address, OpCode.Move, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                var pageAddress = address & ~0xFFFUL;
                adrpOffsets[instruction.Op0Reg] = (ulong)((long)pageAddress + instruction.Op1Imm);
                break;
            case Arm64Mnemonic.LDP when instruction.Op2Kind == Arm64OperandKind.Memory:
                //LDP (dest1, dest2, [mem]) - basically just treat as two loads, with the second offset by the length of the first
                var destRegSize = instruction.Op0Reg switch
                {
                    //vector (128 bit)
                    >= Arm64Register.V0 and <= Arm64Register.V31 => 16, //TODO check if this is accurate
                    //double
                    >= Arm64Register.D0 and <= Arm64Register.D31 => 8,
                    //single
                    >= Arm64Register.S0 and <= Arm64Register.S31 => 4,
                    //half
                    >= Arm64Register.H0 and <= Arm64Register.H31 => 2,
                    //word
                    >= Arm64Register.W0 and <= Arm64Register.W31 => 4,
                    //x
                    >= Arm64Register.X0 and <= Arm64Register.X31 => 8,
                    _ => throw new($"Unknown register size for LDP: {instruction.Op0Reg}")
                };

                var dest1 = ConvertOperand(instruction, 0);
                var dest2 = ConvertOperand(instruction, 1);
                var mem = ConvertOperand(instruction, 2);

                //TODO clean this mess up
                var memInternal = mem as MemoryOperand?;
                var mem2 = new MemoryOperand((Register)memInternal!.Value.Base!, addend: memInternal.Value.Addend + destRegSize);

                Add(address, OpCode.Move, dest1, mem);
                Add(address, OpCode.Move, dest2, mem2);
                break;
            case Arm64Mnemonic.BL:
                if (context.AppContext.MethodsByAddress.TryGetValue(instruction.BranchTarget, out var possibleMethods))
                {
                    if (possibleMethods.Count == 1)
                        AddCall(context, GetReturnRegisterForContext(possibleMethods[0]), address, instruction.BranchTarget);
                    else
                        // TODO: Properly fix this case where branch address is potentially more than 1 method
                        AddCall(context, GetReturnRegisterForContext(context), address, instruction.BranchTarget);
                }
                else
                {
                    // TODO: properly handle unmanaged/API function
                    AddCall(context, GetReturnRegisterForContext(context), address, instruction.BranchTarget);
                }
                break;
            case Arm64Mnemonic.RET:
                var returnRegister = GetReturnRegisterForContext(context);
                if (returnRegister == null)
                    Add(address, OpCode.Return);
                else
                    Add(address, OpCode.Return, returnRegister);
                break;
            case Arm64Mnemonic.B:
                var target = instruction.BranchTarget;

                if (target < context.UnderlyingPointer || target > context.UnderlyingPointer + (ulong)context.RawBytes.Length)
                {
                    //Unconditional branch to outside the method, treat as call (tail-call, specifically) followed by return
                    var returnRegister2 = GetReturnRegisterForContext(context);
                    AddCall(context, returnRegister2, address, target);

                    if (returnRegister2 == null)
                        Add(address, OpCode.Return);
                    else
                        Add(address, OpCode.Return, returnRegister2);
                }
                else
                {
                    Add(address, OpCode.Jump, instruction.BranchTarget);
                }

                break;
            case Arm64Mnemonic.BR:
                // branches unconditionally to an address in a register, with a hint that this is not a subroutine return.
                Add(address, OpCode.IndirectCall, ConvertOperand(instruction, 0));
                break;
            case Arm64Mnemonic.CBNZ:
            case Arm64Mnemonic.CBZ:
                {
                    //Compare and branch if (non-)zero
                    var targetAddr = (ulong)((long)instruction.Address + instruction.Op1Imm);

                    //Compare to zero...
                    Add(address, OpCode.CheckEqual, new Register(null, "Z"), ConvertOperand(instruction, 0), 0);

                    //And jump if (not) equal
                    if (instruction.Mnemonic == Arm64Mnemonic.CBZ)
                    {
                        Add(address, OpCode.ConditionalJump, targetAddr, new Register(null, "Z"));
                    }
                    else
                    {
                        Add(address, OpCode.Not, new Register(null, "TEMP"), new Register(null, "Z"));
                        Add(address, OpCode.ConditionalJump, targetAddr, new Register(null, "TEMP"));
                    }
                }
                break;

            case Arm64Mnemonic.CMP:
                var op1 = ConvertOperand(instruction, 0);
                var op2 = ConvertOperand(instruction, 1);
                var temp = new Register(null, "TEMP");

                Add(address, OpCode.Subtract, temp, op1, op2);
                Add(address, OpCode.CheckEqual, new Register(null, "Z"), temp, 0);
                break;

            case Arm64Mnemonic.TBNZ:
            // TBNZ R<t>, #imm, label
            // test bit and branch if NonZero
            case Arm64Mnemonic.TBZ:
                // TBZ R<t>, #imm, label
                // test bit and branch if Zero
                {
                    var targetAddr = (ulong)((long)instruction.Address + instruction.Op2Imm);
                    var bit = 1 << (int)instruction.Op1Imm;
                    var temp2 = new Register(null, "TEMP");
                    var src = ConvertOperand(instruction, 0);
                    Add(address, OpCode.Move, temp2, src); // temp = src
                    Add(address, OpCode.Move, temp2, bit); // temp = temp & bit
                    Add(address, OpCode.Move, temp2, bit); // result = temp == bit
                    if (instruction.Mnemonic == Arm64Mnemonic.TBNZ)
                    {
                        Add(address, OpCode.ConditionalJump, targetAddr, new Register(null, "Z")); // if (result) goto targetAddr
                    }
                    else
                    {
                        Add(address, OpCode.Not, new Register(null, "TEMP"), new Register(null, "Z"));
                        Add(address, OpCode.ConditionalJump, targetAddr, new Register(null, "TEMP")); // if (result) goto targetAddr
                    }
                }
                break;
            case Arm64Mnemonic.UBFM:
                // UBFM dest, src, #<immr>, #<imms>
                // dest = (src >> #<immr>) & ((1 << #<imms>) - 1)
                {
                    var dest3 = ConvertOperand(instruction, 0);
                    Add(address, OpCode.Move, dest3, ConvertOperand(instruction, 1)); // dest = src
                    Add(address, OpCode.ShiftRight, dest3, dest3, ConvertOperand(instruction, 2)); // dest = dest >> #<immr>
                    var imms = (int)instruction.Op3Imm;
                    Add(address, OpCode.And, dest3, dest3, (1 << imms) - 1); // dest = dest & constexpr { ((1 << #<imms>) - 1) }
                }
                break;

            case Arm64Mnemonic.MUL:
            case Arm64Mnemonic.FMUL:
                //Multiply is (dest, src1, src2)
                Add(address, OpCode.Multiply, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.ADD:
            case Arm64Mnemonic.FADD:
                //Add is (dest, src1, src2)
                Add(address, OpCode.Add, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.SUB:
            case Arm64Mnemonic.FSUB:
                //Sub is (dest, src1, src2)
                Add(address, OpCode.Subtract, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.AND:
                //And is (dest, src1, src2)
                Add(address, OpCode.And, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.ADDS:
            case Arm64Mnemonic.SUBS:
            case Arm64Mnemonic.ANDS:
                var dest = ConvertOperand(instruction, 0);
                var src1 = ConvertOperand(instruction, 1);
                var src2 = ConvertOperand(instruction, 2);

                var opCode = instruction.Mnemonic switch
                {
                    Arm64Mnemonic.ADDS => OpCode.Add,
                    Arm64Mnemonic.SUBS => OpCode.Subtract,
                    Arm64Mnemonic.ANDS => OpCode.And,
                    _ => OpCode.Invalid
                };

                Add(address, opCode, dest, src1, src2);
                Add(address, OpCode.CheckEqual, new Register(null, "Z"), dest, 0);
                break;

            case Arm64Mnemonic.ORR:
                //Orr is (dest, src1, src2)
                Add(address, OpCode.Or, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.EOR:
                //Eor (aka xor) is (dest, src1, src2)
                Add(address, OpCode.Xor, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            default:
                Add(address, OpCode.NotImplemented, $"Instruction {instruction.Mnemonic} not yet implemented.");
                break;
        }
    }

    private object ConvertOperand(Arm64Instruction instruction, int operand)
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
                imm += (long)instruction.Address + 4; //Add 4 to the address to get the address of the next instruction (PC-relative addressing is relative to the address of the next instruction, not the current one

            return imm;
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

            return imm;
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

            return new Register(null, reg.ToString().ToUpperInvariant());
        }

        if (kind == Arm64OperandKind.Memory)
        {
            var reg = instruction.MemBase;
            var offset = instruction.MemOffset;
            var isPreIndexed = instruction.MemIsPreIndexed;

            if (reg == Arm64Register.INVALID)
                //Offset only
                return new MemoryOperand(addend: offset);

            //TODO Handle more stuff here
            return new MemoryOperand(new Register(null, reg.ToString().ToUpperInvariant()), addend: offset);
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

            var name = $"{reg.ToString().ToUpperInvariant()}.{width}{vectorElement.Index}";
            return new Register(null, name);
        }

        return $"<UNIMPLEMENTED OPERAND TYPE {kind}>";
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new NewArm64KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context) => context.RawBytes.Length <= 0 ? "" : string.Join("\n", Disassembler.Disassemble(context.RawBytes.AsSpan(), context.UnderlyingPointer, new Disassembler.Options(true, true, false)).ToList());

    private object? GetReturnRegisterForContext(MethodAnalysisContext context)
    {
        var returnType = context.ReturnType;
        if (returnType.Namespace == nameof(System))
        {
            return returnType.Name switch
            {
                "Void" => null, //Void is no return
                "Double" => new Register(null, nameof(Arm64Register.V0)), //Builtin double is v0
                "Single" => new Register(null, nameof(Arm64Register.V0)), //Builtin float is v0
                _ => new Register(null, nameof(Arm64Register.X0)), //All other system types are x0 like any other pointer
            };
        }

        //TODO Do certain value types have different return registers?

        //Any user type is returned in x0
        return new Register(null, nameof(Arm64Register.X0));
    }

    private List<object> GetArgumentOperandsForCall(MethodAnalysisContext contextBeingCalled)
    {
        var vectorCount = 0;
        var nonVectorCount = 0;

        var ret = new List<object>();

        //Handle 'this' if it's an instance method
        if (!contextBeingCalled.IsStatic)
        {
            ret.Add(new Register(null, nameof(Arm64Register.X0)));
            nonVectorCount++;
        }

        foreach (var parameter in contextBeingCalled.Parameters)
        {
            var paramType = parameter.ParameterType;
            if (paramType.Namespace == nameof(System))
            {
                switch (paramType.Name)
                {
                    case "Single":
                    case "Double":
                        ret.Add(new Register(null, (Arm64Register.V0 + vectorCount++).ToString().ToUpperInvariant()));
                        break;
                    default:
                        ret.Add(new Register(null, (Arm64Register.X0 + nonVectorCount++).ToString().ToUpperInvariant()));
                        break;
                }
            }
            else
            {
                ret.Add(new Register(null, (Arm64Register.X0 + nonVectorCount++).ToString().ToUpperInvariant()));
            }
        }

        return ret;
    }
    
    private List<object> GetArgumentOperandsForCall(MethodAnalysisContext contextBeingAnalyzed, ulong callAddr)
    {
        if (!contextBeingAnalyzed.AppContext.MethodsByAddress.TryGetValue(callAddr, out var methodsAtAddress))
            //TODO
            return [];

        return GetArgumentOperandsForCall(methodsAtAddress.First());
    }
}
