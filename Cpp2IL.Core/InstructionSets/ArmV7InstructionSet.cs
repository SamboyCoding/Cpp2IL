using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.InstructionSets;

public class ArmV7InstructionSet : Cpp2IlInstructionSet
{
    public override BinarySlice GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        var slice = ArmV7Utils.TryGetMethodBodyBytesFast(context.AppContext.Binary, context.UnderlyingPointer, isAttributeGenerator);
        if (slice.Length > 0)
            return slice;

        var instructions = ArmV7Utils.GetArmV7MethodBodyAtVirtualAddress(context.AppContext.Binary, context.UnderlyingPointer);

        return new BinarySlice(instructions.SelectMany(i => i.Bytes).ToArray());
    }

    public override List<Instruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        return [];
    }

    public override List<object> GetParameterOperandsFromMethod(MethodAnalysisContext context)
    {
        return [];
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        //TODO Fix
        return new Arm64KeyFunctionAddresses();
    }

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        var sb = new StringBuilder();

        var instructions = ArmV7Utils.GetArmV7MethodBodyAtVirtualAddress(context.AppContext.Binary, context.UnderlyingPointer);

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
