using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Disarm;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Disarm.InternalDisassembly;
using LibCpp2IL;

namespace Cpp2IL.Core.InstructionSets;

public class NewArmV8InstructionSet : Cpp2IlInstructionSet
{
    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context is not ConcreteGenericMethodAnalysisContext)
        {
            //Managed method or attr gen => grab raw byte range between a and b
            var startOfNextFunction = (int) MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer);
            var ptrAsInt = (int) context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return LibCpp2IlMain.Binary!.GetRawBinaryContent().AsMemory(ptrAsInt, count);
        }
        
        var result = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);
        var endVa = result.LastValid().Address + 4;

        var start = (int) context.AppContext.Binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
        var end = (int) context.AppContext.Binary.MapVirtualAddressToRaw(endVa);
        
        //Sanity check
        if (start < 0 || end < 0 || start >= context.AppContext.Binary.RawLength || end >= context.AppContext.Binary.RawLength)
            throw new Exception($"Failed to map virtual address 0x{context.UnderlyingPointer:X} to raw address for method {context!.DeclaringType?.FullName}/{context.Name} - start: 0x{start:X}, end: 0x{end:X} are out of bounds for length {context.AppContext.Binary.RawLength}.");
        
        return context.AppContext.Binary.GetRawBinaryContent().AsMemory(start, end - start);
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var insns = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);
        
        var builder = new IsilBuilder();

        foreach (var instruction in insns)
        {
            ConvertInstructionStatement(instruction, builder, context);
        }

        builder.FixJumps();

        return builder.BackingStatementList;
    }

    private void ConvertInstructionStatement(Arm64Instruction instruction, IsilBuilder builder, MethodAnalysisContext context)
    {
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
                    var operate= ConvertOperand(instruction, 1);
                    if (operate.Data is IsilMemoryOperand operand)
                    {
                        var register=  operand.Base.Value;
                        // X19= X19, #0x30
                        builder.Move(instruction.Address,register,operate);
                        //X8 = X19
                        builder.Move(instruction.Address,ConvertOperand(instruction,0),register);
                    }
                    else
                    {
                        // use default
                        builder.Move(instruction.Address, ConvertOperand(instruction, 0), operate);
                    }
                }
                else
                {
                    builder.Move(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                }
                break;
            case Arm64Mnemonic.MOVN:
                {
                    // dest = ~src
                    var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                    builder.Move(instruction.Address, temp, ConvertOperand(instruction, 1));
                    builder.Not(instruction.Address, temp);
                    builder.Move(instruction.Address, ConvertOperand(instruction, 0), temp);
                }
                break;
            case Arm64Mnemonic.STR:
            case Arm64Mnemonic.STUR: // unscaled
            case Arm64Mnemonic.STRB:
                //Store is (src, dest)
                builder.Move(instruction.Address, ConvertOperand(instruction, 1), ConvertOperand(instruction, 0));
                break;
            case Arm64Mnemonic.STP:
                // store pair of registers (reg1, reg2, dest)
                {
                    var dest = ConvertOperand(instruction, 2);
                    if (dest.Data is IsilRegisterOperand { RegisterName: "X31" }) // if stack
                    {
                        builder.Move(instruction.Address, dest, ConvertOperand(instruction, 0));
                        builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1));
                    }
                    else if (dest.Data is IsilMemoryOperand memory)
                    {
                        var firstRegister = ConvertOperand(instruction, 0);
                        long size = ((IsilRegisterOperand)firstRegister.Data).RegisterName[0] == 'W' ? 4 : 8;
                        builder.Move(instruction.Address, dest, firstRegister); // [REG + offset] = REG1
                        memory = new IsilMemoryOperand(memory.Base!.Value, memory.Addend + size);
                        dest = InstructionSetIndependentOperand.MakeMemory(memory);
                        builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1)); // [REG + offset + size] = REG2
                    }
                    else // reg pointer
                    {
                        var firstRegister = ConvertOperand(instruction, 0);
                        long size = ((IsilRegisterOperand)firstRegister.Data).RegisterName[0] == 'W' ? 4 : 8;
                        builder.Move(instruction.Address, dest, firstRegister);
                        builder.Add(instruction.Address, dest, dest, InstructionSetIndependentOperand.MakeImmediate(size));
                        builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1));
                    }
                }
                break;
            case Arm64Mnemonic.ADRP:
                //Just handle as a move
                builder.Move(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
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
                var memInternal = mem.Data as IsilMemoryOperand?;
                var mem2 = new IsilMemoryOperand(memInternal!.Value.Base!.Value, memInternal.Value.Addend + destRegSize);
                
                builder.Move(instruction.Address, dest1, mem);
                builder.Move(instruction.Address, dest2, InstructionSetIndependentOperand.MakeMemory(mem2));
                break;
            case Arm64Mnemonic.BL:
                builder.Call(instruction.Address, instruction.BranchTarget, GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
                break;
            case Arm64Mnemonic.RET:
                builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                break;
            case Arm64Mnemonic.B:
                var target = instruction.BranchTarget;
                
                if (target < context.UnderlyingPointer || target > context.UnderlyingPointer + (ulong)context.RawBytes.Length)
                {
                    //Unconditional branch to outside the method, treat as call (tail-call, specifically) followed by return
                    builder.Call(instruction.Address, instruction.BranchTarget, GetArgumentOperandsForCall(context, instruction.BranchTarget).ToArray());
                    builder.Return(instruction.Address, GetReturnRegisterForContext(context));
                }

                break;
            case Arm64Mnemonic.BR:
                // branches unconditionally to an address in a register, with a hint that this is not a subroutine return.
                builder.CallRegister(instruction.Address, ConvertOperand(instruction, 0), noReturn: true);
                break;
            case Arm64Mnemonic.CBNZ:
            case Arm64Mnemonic.CBZ:
                {
                    //Compare and branch if (non-)zero
                    var targetAddr = (ulong)((long)instruction.Address + instruction.Op1Imm);

                    //Compare to zero...
                    builder.Compare(instruction.Address, ConvertOperand(instruction, 0), InstructionSetIndependentOperand.MakeImmediate(0));

                    //And jump if (not) equal
                    if (instruction.Mnemonic == Arm64Mnemonic.CBZ)
                        builder.JumpIfEqual(instruction.Address, targetAddr);
                    else
                        builder.JumpIfNotEqual(instruction.Address, targetAddr);
                }
                break;

            case Arm64Mnemonic.CMP:
                // Compare: set flag (N or Z or C or V) = (reg1 - reg2)
                // builder.Compare(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 0));
                goto default;

            case Arm64Mnemonic.TBNZ:
                // TBNZ R<t>, #imm, label
                // test bit and branch if NonZero
            case Arm64Mnemonic.TBZ:
                // TBZ R<t>, #imm, label
                // test bit and branch if Zero
                {
                    var targetAddr = (ulong)((long)instruction.Address + instruction.Op2Imm);
                    var bit = InstructionSetIndependentOperand.MakeImmediate(1 << (int)instruction.Op1Imm);
                    var temp = InstructionSetIndependentOperand.MakeRegister("TEMP");
                    var src = ConvertOperand(instruction, 0);
                    builder.Move(instruction.Address, temp, src); // temp = src
                    builder.And(instruction.Address, temp, temp, bit); // temp = temp & bit
                    builder.Compare(instruction.Address, temp, bit); // result = temp == bit
                    if (instruction.Mnemonic == Arm64Mnemonic.TBNZ)
                        builder.JumpIfEqual(instruction.Address, targetAddr); // if (result) goto targetAddr
                    else
                        builder.JumpIfNotEqual(instruction.Address, targetAddr); // if (result) goto targetAddr
                }
                break;
            case Arm64Mnemonic.UBFM:
                // UBFM dest, src, #<immr>, #<imms>
                // dest = (src >> #<immr>) & ((1 << #<imms>) - 1)
                {
                    var dest = ConvertOperand(instruction, 0);
                    builder.Move(instruction.Address, dest, ConvertOperand(instruction, 1)); // dest = src
                    builder.ShiftRight(instruction.Address, dest, ConvertOperand(instruction, 2)); // dest >> #<immr>
                    var imms = (int)instruction.Op3Imm;
                    builder.And(instruction.Address, dest, dest, 
                        InstructionSetIndependentOperand.MakeImmediate((1 << imms) - 1)); // dest & constexpr { ((1 << #<imms>) - 1) }
                }
                break;

            case Arm64Mnemonic.MUL:
            case Arm64Mnemonic.FMUL:
                //Multiply is (dest, src1, src2)
                builder.Multiply(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.ADD:
            case Arm64Mnemonic.ADDS: // settings flags
            case Arm64Mnemonic.FADD:
                //Add is (dest, src1, src2)
                builder.Add(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.SUB:
            case Arm64Mnemonic.SUBS: // settings flags
            case Arm64Mnemonic.FSUB:
                //Sub is (dest, src1, src2)
                builder.Subtract(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.AND:
            case Arm64Mnemonic.ANDS:
                //And is (dest, src1, src2)
                builder.And(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.ORR:
                //Orr is (dest, src1, src2)
                builder.Or(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            case Arm64Mnemonic.EOR:
                //Eor (aka xor) is (dest, src1, src2)
                builder.Xor(instruction.Address, ConvertOperand(instruction, 0), ConvertOperand(instruction, 1), ConvertOperand(instruction, 2));
                break;

            default:
                builder.NotImplemented(instruction.Address, $"Instruction {instruction.Mnemonic} not yet implemented.");
                break;
        }
    }

    private InstructionSetIndependentOperand ConvertOperand(Arm64Instruction instruction, int operand)
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
            
            if(kind == Arm64OperandKind.ImmediatePcRelative)
                imm += (long) instruction.Address + 4; //Add 4 to the address to get the address of the next instruction (PC-relative addressing is relative to the address of the next instruction, not the current one

            return InstructionSetIndependentOperand.MakeImmediate(imm);
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

            return InstructionSetIndependentOperand.MakeImmediate(imm);
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
            
            return InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToUpperInvariant());
        }

        if (kind == Arm64OperandKind.Memory)
        {
            var reg = instruction.MemBase;
            var offset = instruction.MemOffset;
            var isPreIndexed = instruction.MemIsPreIndexed;
            
            if(reg == Arm64Register.INVALID)
                //Offset only
                return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(offset));

            //TODO Handle more stuff here
            return InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(
                InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToUpperInvariant()),
                offset));
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
                Arm64VectorElementWidth.B => IsilVectorRegisterElementOperand.VectorElementWidth.B,
                Arm64VectorElementWidth.H => IsilVectorRegisterElementOperand.VectorElementWidth.H,
                Arm64VectorElementWidth.S => IsilVectorRegisterElementOperand.VectorElementWidth.S,
                Arm64VectorElementWidth.D => IsilVectorRegisterElementOperand.VectorElementWidth.D,
                _ => throw new ArgumentOutOfRangeException(nameof(vectorElement.Width), $"Unknown vector element width {vectorElement.Width}")
            };
            
            //<Reg>.<Width>[<Index>]
            return InstructionSetIndependentOperand.MakeVectorElement(reg.ToString().ToUpperInvariant(), width, vectorElement.Index);
        }

        return InstructionSetIndependentOperand.MakeImmediate($"<UNIMPLEMENTED OPERAND TYPE {kind}>");
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new NewArm64KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context) => context.RawBytes.Span.Length <= 0 ? "" : string.Join("\n", Disassembler.Disassemble(context.RawBytes.Span, context.UnderlyingPointer, new Disassembler.Options(true, true, false)).ToList());

    private InstructionSetIndependentOperand? GetReturnRegisterForContext(MethodAnalysisContext context)
    {
        var returnType = context.ReturnTypeContext;
        if (returnType.Namespace == nameof(System))
        {
            return returnType.Name switch
            {
                "Void" => null, //Void is no return
                "Double" => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.V0)), //Builtin double is v0
                "Single" => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.V0)), //Builtin float is v0
                _ => InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0)), //All other system types are x0 like any other pointer
            };
        }

        //TODO Do certain value types have different return registers?
        
        //Any user type is returned in x0
        return InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0));
    }

    private List<InstructionSetIndependentOperand> GetArgumentOperandsForCall(MethodAnalysisContext contextBeingAnalyzed, ulong callAddr)
    {
        if (!contextBeingAnalyzed.AppContext.MethodsByAddress.TryGetValue(callAddr, out var methodsAtAddress))
            //TODO
            return new List<InstructionSetIndependentOperand>();
        
        //For the sake of arguments, all we care about is the first method at the address, because they'll only be shared if they have the same signature.
        var contextBeingCalled = methodsAtAddress.First();

        var vectorCount = 0;
        var nonVectorCount = 0;
        
        var ret = new List<InstructionSetIndependentOperand>();
        
        //Handle 'this' if it's an instance method
        if (!contextBeingCalled.IsStatic)
        {
            ret.Add(InstructionSetIndependentOperand.MakeRegister(nameof(Arm64Register.X0)));
            nonVectorCount++;
        }
        
        foreach (var parameter in contextBeingCalled.Parameters)
        {
            var paramType = parameter.ParameterTypeContext;
            if (paramType.Namespace == nameof(System))
            {
                switch (paramType.Name)
                {
                    case "Single":
                    case "Double":
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.V0 + vectorCount++).ToString().ToUpperInvariant()));
                        break;
                    default:
                        ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.X0 + nonVectorCount++).ToString().ToUpperInvariant()));
                        break;
                }
            }
            else
            {
                ret.Add(InstructionSetIndependentOperand.MakeRegister((Arm64Register.X0 + nonVectorCount++).ToString().ToUpperInvariant()));
            }
        }
        
        return ret;
    }
}
