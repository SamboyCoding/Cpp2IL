using System;
using System.Collections.Generic;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using WasmDisassembler;

namespace Cpp2IL.Core.InstructionSets;

public class WasmInstructionSet : Cpp2IlInstructionSet
{
    public virtual IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        return null!;
    }

    public override Memory<byte> GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context.Definition is { } methodDefinition)
        {
            var wasmDef = WasmUtils.TryGetWasmDefinition(methodDefinition);

            if (wasmDef == null)
            {
                Logger.WarnNewline($"Could not find WASM definition for method {methodDefinition.Name}, probably incorrect signature calculation", "WasmInstructionSet");
                return Array.Empty<byte>();
            }
            
            if(wasmDef.AssociatedFunctionBody == null)
                throw new($"WASM definition {wasmDef}, resolved from MethodAnalysisContext {context} has no associated function body");

            return wasmDef.AssociatedFunctionBody.Instructions;
        }

        return Array.Empty<byte>();
    }
    
    public override List<InstructionSetIndependentInstruction> GetIsilFromMethod(MethodAnalysisContext context)
    {
        return new();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        return new WasmKeyFunctionAddresses();
    }

    public override string PrintAssembly(MethodAnalysisContext context)
    {
        if (context.Definition is not { } methodDefinition)
            return string.Empty;

        var def = WasmUtils.GetWasmDefinition(methodDefinition);
        var disassembled = Disassembler.Disassemble(def.AssociatedFunctionBody!.Instructions, (uint) context.UnderlyingPointer);

        return string.Join("\n", disassembled);
    }
}
