using System;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.CorePlugin;

public class AttributeInjectorProcessingLayer : Cpp2IlProcessingLayer
{
    public override string Name => "Attribute Injector";
    public override string Id => "attributeinjector";

    public override void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null)
    {
        var stringType = appContext.GetAssemblyByName("mscorlib")?.GetTypeByFullName("System.String") ?? throw new("Failed to get System.String");
        var voidType = appContext.GetAssemblyByName("mscorlib")?.GetTypeByFullName("System.Void") ?? throw new("Failed to get System.Void");

        appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "AnalysisFailedException")
            .InjectMethod(".ctor", false, voidType, stringType); //TODO Make specialname | rtspecialname
    }
}