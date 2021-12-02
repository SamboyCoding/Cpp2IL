using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Model;

public class X86InstructionSet : BaseInstructionSet
{
    public override IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        var methodBody = X86Utils.GetManagedMethodBody(context.Definition);
        return new X86ControlFlowGraph(methodBody.ToList());
    }
}