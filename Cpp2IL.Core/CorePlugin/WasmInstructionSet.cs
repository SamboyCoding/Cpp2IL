using System;
using System.Collections.Generic;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using WasmDisassembler;

namespace Cpp2IL.Core.CorePlugin;

public class WasmInstructionSet : BaseInstructionSet
{
    public override IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        return null;
    }

    public override byte[] GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        if (context.Definition is { } methodDefinition)
            return WasmUtils.GetWasmDefinition(methodDefinition).AssociatedFunctionBody?.Instructions ?? throw new ArgumentException("Attempting to get raw bytes for an imported method.");
        
        return Array.Empty<byte>();
    }

    public override List<InstructionSetIndependentNode> ControlFlowGraphToISIL(IControlFlowGraph graph, MethodAnalysisContext context)
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