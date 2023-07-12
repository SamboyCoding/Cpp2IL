using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Disarm;
using LibCpp2IL;

namespace Cpp2IL.InstructionSets.ArmV8;

public class ArmV8InstructionSet : Cpp2IlInstructionSet
{
    public static void RegisterInstructionSet()
    {
        InstructionSetRegistry.RegisterInstructionSet<ArmV8InstructionSet>(DefaultInstructionSets.ARM_V8);
    }

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context is not ConcreteGenericMethodAnalysisContext)
        {
            //Managed method or attr gen => grab raw byte range between a and b
            var startOfNextFunction = (int) MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer) - 1;
            var ptrAsInt = (int) context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return LibCpp2IlMain.Binary!.GetRawBinaryContent().AsMemory(ptrAsInt, count);
        }

        ArmV8Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer, out var endVirtualAddress);

        var start = (int) context.AppContext.Binary.MapVirtualAddressToRaw(context.UnderlyingPointer);
        var end = (int) context.AppContext.Binary.MapVirtualAddressToRaw(endVirtualAddress);

        return context.AppContext.Binary.GetRawBinaryContent().AsMemory(start, end - start);
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        var instructions = ArmV8Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer, out var endVirtualAddress);

        throw new NotImplementedException();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        return new ArmV8KeyFunctionAddresses();
    }

    public override string PrintAssembly(MethodAnalysisContext context) => string.Join("\n", Disassembler.Disassemble(context.RawBytes, context.UnderlyingPointer));
}
