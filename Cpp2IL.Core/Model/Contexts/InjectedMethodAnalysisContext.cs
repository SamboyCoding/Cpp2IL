using System.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedMethodAnalysisContext : MethodAnalysisContext
{
    public override ulong UnderlyingPointer => 0;

    public override string DefaultName { get; }

    public override TypeAnalysisContext DefaultReturnType { get; }

    public override MethodAttributes DefaultAttributes { get; }

    public override MethodImplAttributes DefaultImplAttributes { get; }

    protected override bool IsInjected => true;

    protected override int CustomAttributeIndex => -1;

    public InjectedMethodAnalysisContext(
        TypeAnalysisContext parent,
        string name,
        TypeAnalysisContext returnType,
        MethodAttributes attributes,
        TypeAnalysisContext[] injectedParameterTypes,
        string[]? injectedParameterNames = null,
        ParameterAttributes[]? injectedParameterAttributes = null,
        MethodImplAttributes defaultImplAttributes = MethodImplAttributes.Managed) : base(null, parent)
    {
        DefaultName = name;
        DefaultReturnType = returnType;
        DefaultAttributes = attributes;

        for (var i = 0; i < injectedParameterTypes.Length; i++)
        {
            var injectedParameterType = injectedParameterTypes[i];
            var injectedParameterName = injectedParameterNames?[i];
            var injectedParameterAttribute = injectedParameterAttributes?[i] ?? ParameterAttributes.None;

            Parameters.Add(new InjectedParameterAnalysisContext(injectedParameterName, injectedParameterType, injectedParameterAttribute, i, this));
        }

        DefaultImplAttributes = defaultImplAttributes;
    }
}
