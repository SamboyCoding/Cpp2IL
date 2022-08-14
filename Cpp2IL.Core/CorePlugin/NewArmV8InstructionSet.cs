using System;
using System.Collections.Generic;
using Arm64Disassembler;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.CorePlugin;

public class NewArmV8InstructionSet : Cpp2IlInstructionSet
{
    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (true || context is not ConcreteGenericMethodAnalysisContext)
        {
            //Managed method or attr gen => grab raw byte range between a and b
            var startOfNextFunction = (int) MiscUtils.GetAddressOfNextFunctionStart(context.UnderlyingPointer) - 1;
            var ptrAsInt = (int) context.UnderlyingPointer;
            var count = startOfNextFunction - ptrAsInt;

            if (startOfNextFunction > 0)
                return LibCpp2IlMain.Binary!.GetRawBinaryContent().AsMemory(ptrAsInt, count);
        }
        
        var result = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(context.UnderlyingPointer);

        return result.RawBytes.ToArray();
    }

    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        //TODO
        return new();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        //TODO Make this use the new arm64 disassembler once it's ready
        return new Arm64KeyFunctionAddresses();
    }

    public override string PrintAssembly(MethodAnalysisContext context) => string.Join("\n", Disassembler.Disassemble(context.RawBytes.Span, context.UnderlyingPointer).Instructions);
}