using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;

namespace Cpp2IL.InstructionSets.ArmV7;

public class ArmV7InstructionSet : Cpp2IlInstructionSet
{
    public static void RegisterInstructionSet()
    {
        InstructionSetRegistry.RegisterInstructionSet<ArmV7InstructionSet>(DefaultInstructionSets.ARM_V7);
    }

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (ArmV7Utils.TryGetMethodBodyBytesFast(context.UnderlyingPointer, context is AttributeGeneratorMethodAnalysisContext) is { } ret)
            return ret;

        ArmV7Utils.DisassembleManagedMethod(context.UnderlyingPointer, out var endVirtualAddress);

        var start = (int)context.AppContext.Binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
        var end = (int)context.AppContext.Binary.MapVirtualAddressToRaw(endVirtualAddress);

        return context.AppContext.Binary.GetRawBinaryContent().AsMemory(start, end - start);
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        throw new NotImplementedException();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        return new ArmV7KeyFunctionAddresses();
    }

    public override unsafe string PrintAssembly(MethodAnalysisContext context)
    {
        var sb = new StringBuilder();
        var first = true;

        using (ArmV7Utils.Disassembler.AllocInstruction(out var instruction))
        {
            fixed (byte* code = context.RawBytes.Span)
            {
                var size = (nuint)context.RawBytes.Length;
                var address = context.UnderlyingPointer;
                while (ArmV7Utils.Disassembler.UnsafeIterate(&code, &size, &address, instruction))
                {
                    if (!first)
                    {
                        sb.AppendLine();
                        first = false;
                    }

                    sb.Append("0x").Append(address.ToString("X")).Append(" ").AppendLine(instruction->ToString());
                }
            }
        }

        return sb.ToString();
    }
}
