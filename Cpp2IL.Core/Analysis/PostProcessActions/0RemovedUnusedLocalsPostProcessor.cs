// #define PRINT_UNUSED_LOCAL_DATA

using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public class RemovedUnusedLocalsPostProcessor<T> : PostProcessor<T>
    {
        public override void PostProcess(MethodAnalysis<T> analysis)
        {
            var unused = analysis.Locals.Where(l => !analysis.FunctionArgumentLocals.Contains(l) && analysis.Actions.All(a => !a.GetUsedLocals().Contains(l))).ToList();
#if PRINT_UNUSED_LOCAL_DATA
            Console.WriteLine($"Found {unused.Count} unused locals for method {definition}: ");
#endif

            foreach (var unusedLocal in unused)
            {
#if PRINT_UNUSED_LOCAL_DATA
                Console.WriteLine($"\t{unusedLocal.Name}");
#endif
                analysis.Actions = analysis.Actions.Where(a => !a.GetRegisteredLocalsWithoutSideEffects().Contains(unusedLocal)).ToList();
            }

            analysis.Locals.RemoveAll(l => unused.Contains(l));
        }
    }
}