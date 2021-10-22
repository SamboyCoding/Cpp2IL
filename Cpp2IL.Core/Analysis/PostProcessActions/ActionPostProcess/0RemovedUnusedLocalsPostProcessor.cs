// #define PRINT_UNUSED_LOCAL_DATA

using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;

namespace Cpp2IL.Core.Analysis.PostProcessActions
{
    public class RemovedUnusedLocalsPostProcessor<T> : PostProcessor<T>
    {
        public override void PostProcess(MethodAnalysis<T> analysis)
        {
            var unused = analysis.UnusedLocals;
#if PRINT_UNUSED_LOCAL_DATA
            Console.WriteLine($"Found {unused.Count} unused locals for method {definition}: ");
#endif

            var toRemove = new List<BaseAction<T>>();
            foreach (var unusedLocal in unused)
            {
                foreach (var analysisAction in analysis.Actions)
                {
                    if(analysisAction.RegisteredLocalsWithoutSideEffects.Contains(unusedLocal))
                        toRemove.Add(analysisAction);
                }
            }
            
            toRemove.ForEach(a => analysis.Actions.Remove(a));
            unused.ForEach(l => analysis.Locals.Remove(l));
        }
    }
}