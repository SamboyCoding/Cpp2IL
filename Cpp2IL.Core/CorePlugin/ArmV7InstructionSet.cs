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

namespace Cpp2IL.Core.CorePlugin;

public class ArmV7InstructionSet : Cpp2IlInstructionSet
{
    public virtual IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        return null;
    }

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (ArmV7Utils.TryGetMethodBodyBytesFast(context.UnderlyingPointer, context is AttributeGeneratorMethodAnalysisContext) is { } ret)
            return ret;
        
        var instructions = ArmV7Utils.GetArmV7MethodBodyAtVirtualAddress(context.UnderlyingPointer);

        return instructions.SelectMany(i => i.Bytes).ToArray();
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        return new();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        //TODO Fix
        return new Arm64KeyFunctionAddresses();
    }

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        var sb = new StringBuilder();
        
        var instructions = ArmV7Utils.GetArmV7MethodBodyAtVirtualAddress(context.UnderlyingPointer);

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