using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.Model;

public class X86InstructionSet : BaseInstructionSet
{
    public override IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        List<Instruction> instructions;

        if (context is not AttributeGeneratorMethodAnalysisContext)
            instructions = X86Utils.GetManagedMethodBody(context.Definition!).ToList();
        else
        {
            var rawMethodBody = GetRawBytesForMethod(context, context is AttributeGeneratorMethodAnalysisContext);
            instructions = X86Utils.Disassemble(rawMethodBody, context.UnderlyingPointer).ToList();
        }

        return new X86ControlFlowGraph(instructions, context.AppContext.Binary.is32Bit, context.AppContext.GetOrCreateKeyFunctionAddresses());
    }

    public override byte[] GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override List<InstructionSetIndependentNode> ControlFlowGraphToISIL(IControlFlowGraph graph, MethodAnalysisContext context)
    {
        //TODO Implement me!
        return new();
    }
}