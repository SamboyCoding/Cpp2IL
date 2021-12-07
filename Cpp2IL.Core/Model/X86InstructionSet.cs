using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Model;

public class X86InstructionSet : BaseInstructionSet
{
    public override IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        if (context is not AttributeGeneratorMethodAnalysisContext)
            return new X86ControlFlowGraph(X86Utils.GetManagedMethodBody(context.Definition!).ToList());
        
        var rawMethodBody = GetRawBytesForMethod(context, context is AttributeGeneratorMethodAnalysisContext);
        var methodBody = X86Utils.Disassemble(rawMethodBody, context.UnderlyingPointer);
        
        return new X86ControlFlowGraph(methodBody.ToList());
    }

    public override byte[] GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator)
    {
        // if (!isAttributeGenerator)
        return X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator);
        
        // X86Utils.GetMethodBodyAtVirtAddressNew(context.UnderlyingPointer, false, out var ret);
        // return ret;
    }

    public override List<InstructionSetIndependentNode> ControlFlowGraphToISIL(IControlFlowGraph graph, MethodAnalysisContext context)
    {
        //TODO Implement me!
        return new();
    }
}