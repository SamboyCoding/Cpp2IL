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
        appContext.InjectTypeIntoAllAssemblies("Cpp2ILInjected", "AnalysisFailedException", appContext.SystemTypes.SystemExceptionType)
            .InjectConstructor( false, appContext.SystemTypes.SystemStringType);
    }
}