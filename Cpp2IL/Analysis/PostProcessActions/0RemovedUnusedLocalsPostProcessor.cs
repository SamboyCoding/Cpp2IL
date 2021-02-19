using System;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;

namespace Cpp2IL.Analysis.PostProcessActions
{
    public class RemovedUnusedLocalsPostProcessor : PostProcessor
    {
        public override void PostProcess(MethodAnalysis analysis, MethodDefinition definition)
        {
            var unused = analysis.Locals.Where(l => !analysis.FunctionArgumentLocals.Contains(l) && analysis.Actions.All(a => !a.GetUsedLocals().Contains(l))).ToList();
            Console.WriteLine($"Found {unused.Count} unused locals for method {definition}");
            
            foreach (var unusedLocal in unused)
            {
                analysis.Actions = analysis.Actions.Where(a => !a.GetRegisteredLocalsWithoutSideEffects().Contains(unusedLocal)).ToList();
            }

            analysis.Locals.RemoveAll(l => unused.Contains(l));
        }
    }
}