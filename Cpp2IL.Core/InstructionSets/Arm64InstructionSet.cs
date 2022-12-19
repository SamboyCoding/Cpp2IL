using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.InstructionSets;

public class Arm64InstructionSet : Cpp2IlInstructionSet
{
    public virtual IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        return null!;
    }

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        //Avoid use of capstone where possible
        if (true || context is not ConcreteGenericMethodAnalysisContext)
        {
            //Managed method or attr gen => grab raw byte range between a and b
            var startOfNextFunction = (int) MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer) - 1;
            var ptrAsInt = (int) context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return LibCpp2IlMain.Binary!.GetRawBinaryContent().AsMemory(ptrAsInt, count);
        }
        
        var instructions = Arm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);

        return instructions.SelectMany(i => i.Bytes).ToArray();
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        return new();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new Arm64KeyFunctionAddresses();

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        var sb = new StringBuilder();
        
        var instructions = Arm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);

        var first = true;
        foreach (var instruction in instructions)
        {
            if (!first)
                sb.AppendLine();
            
            first = false;
            sb.Append("0x").Append(instruction.Address.ToString("X")).Append(" ").Append(instruction.Mnemonic).Append(" ").Append(instruction.Operand);
        }

        return sb.ToString();
    }
}
