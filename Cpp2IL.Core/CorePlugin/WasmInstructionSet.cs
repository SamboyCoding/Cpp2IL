using System.Collections.Generic;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.CorePlugin;

public class WasmInstructionSet : BaseInstructionSet
{
    public override IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        return null;
    }

    public override byte[] GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        return new byte[0];
    }

    public override List<InstructionSetIndependentNode> ControlFlowGraphToISIL(IControlFlowGraph graph, MethodAnalysisContext context)
    {
        return new();
    }

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance()
    {
        return new WasmKeyFunctionAddresses();
    }
}