using System;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using Arm64Instruction = Disarm.Arm64Instruction;

namespace Cpp2IL.Core.InstructionSets;

public class Arm64InsFixer
{
    

    
    
    public static bool CheckFix( Arm64Instruction instruction,IsilBuilder builder)
    {
        var address= Cpp2IlApi.CurrentAppContext.Binary.MapVirtualAddressToRaw(instruction.Address);
        var data=   Cpp2IlApi.CurrentAppContext.Binary.GetRawBinaryContent().AsMemory((int)address, 4);
        using (var disassembler= new CapstoneArm64Disassembler(Arm64DisassembleMode.Arm))
        {
            disassembler.EnableInstructionDetails = true;
            disassembler.EnableSkipDataMode = true;
            disassembler.DisassembleSyntax = DisassembleSyntax.Intel;
            var instructions= disassembler.Disassemble(data.ToArray());
            foreach (var ins in instructions)
            {
                 if (ins.ToString()!=Arm64InsExtensions.FixString(instruction))
                 {
                     // Logger.InfoNewline("===================================LDR " + ins + "   =  ori " +Arm64InsExtensions.FixString(instruction));
                     
                     Logger.ErrorNewline("find Error parser ins "+ins  +"  =  error "+Arm64InsExtensions.FixString(instruction) +" addr "+instruction.Address.ToString("X"));
                     CreateFixBuilder(builder,ins,instruction.Address);
                     return true;
                 }
            }
        }

        return false;
    }

    private static void CreateFixBuilder(IsilBuilder builder, Gee.External.Capstone.Arm64.Arm64Instruction ins,
        ulong address)
    {
        if (ins.Mnemonic=="ldr")
        {
            var baseReg = ins.Details.Operands[0];
            //memory reg
            var memoryReg = ins.Details.Operands[1].Memory.Base.Name.ToUpperInvariant();
            builder.Move((ulong)address,InstructionSetIndependentOperand.MakeRegister(baseReg.Register.Name.ToUpperInvariant()),
                InstructionSetIndependentOperand.MakeMemory(new IsilMemoryOperand(
                    InstructionSetIndependentOperand.MakeRegister(memoryReg))));
            builder.Add((ulong)address,InstructionSetIndependentOperand.MakeRegister(memoryReg),
                InstructionSetIndependentOperand.MakeRegister(memoryReg),InstructionSetIndependentOperand.MakeImmediate(ins.Details.Operands[2].Immediate));
        }
        else
        {
            throw new Exception("not support ins "+ins.Mnemonic);
        }
    }
}
