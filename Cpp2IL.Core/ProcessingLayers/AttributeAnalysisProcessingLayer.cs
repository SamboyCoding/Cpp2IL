using System;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ProcessingLayers;

public class AttributeAnalysisProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "CustomAttribute Analyzer";
    public override string Id => "attributeanalyzer";
    
    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var total = appContext.Assemblies.Count + appContext.AllTypes.Select(t => 1 + t.Events.Count + t.Fields.Count + t.Methods.Count + t.Properties.Count).Sum();

        int count = 0;
        appContext.Assemblies.ForEach(a => AnalyzeAndRaise(a, ref count, total, progressCallback));

        //TODO look into making this parallel
        foreach (var type in appContext.AllTypes)
        {
            AnalyzeAndRaise(type, ref count, total, progressCallback);
            type.Events.ForEach(e => AnalyzeAndRaise(e, ref count, total, progressCallback));
            type.Fields.ForEach(f => AnalyzeAndRaise(f, ref count, total, progressCallback));
            type.Methods.ForEach(m =>
            {
                AnalyzeAndRaise(m, ref count, total, progressCallback);
                m.Parameters.ForEach(p => AnalyzeAndRaise(p, ref count, total, progressCallback));
            });
            type.Properties.ForEach(p => AnalyzeAndRaise(p, ref count, total, progressCallback));
        }
    }

    private void AnalyzeAndRaise(HasCustomAttributes toAnalyze, ref int count, int total, Action<int, int>? progressCallback)
    {
        toAnalyze.AnalyzeCustomAttributeData();
        count++;
        progressCallback?.Invoke(count, total);
    }
}
