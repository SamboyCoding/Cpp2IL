using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public class RemoveUnusedLocals : IAction
{
    public void Apply(MethodAnalysisContext method)
    {
        var cfg = method.ControlFlowGraph!;
        cfg.BuildUseDefLists();

        for (var i = 0; i < method.Locals.Count; i++)
        {
            var local = method.Locals[i];

            if (cfg.Blocks.Any(b => b.Use.Contains(local) || b.Def.Contains(local)))
                continue;

            method.Locals.Remove(local);
            i--;
        }
    }
}
