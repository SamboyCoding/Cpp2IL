using System;
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
        ReadOnlySpan<TypeAnalysisContext> injectedParameterTypes,
        ReadOnlySpan<string> injectedParameterNames = default,
        ReadOnlySpan<ParameterAttributes> injectedParameterAttributes = default,
        MethodImplAttributes defaultImplAttributes = MethodImplAttributes.Managed) : base(null, parent)
    {
        DefaultName = name;
        DefaultReturnType = returnType;
        DefaultAttributes = attributes;

        var hasParameterNames = !injectedParameterNames.IsEmpty;
        var hasParameterAttributes = !injectedParameterAttributes.IsEmpty;

        if (hasParameterNames && injectedParameterNames.Length != injectedParameterTypes.Length)
            throw new ArgumentException("Length of injected parameter names must match length of injected parameter types.", nameof(injectedParameterNames));
        if (hasParameterAttributes && injectedParameterAttributes.Length != injectedParameterTypes.Length)
            throw new ArgumentException("Length of injected parameter attributes must match length of injected parameter types.", nameof(injectedParameterAttributes));

        for (var i = 0; i < injectedParameterTypes.Length; i++)
        {
            var injectedParameterType = injectedParameterTypes[i];
            var injectedParameterName = hasParameterNames ? injectedParameterNames[i] : null;
            var injectedParameterAttribute = hasParameterAttributes ? injectedParameterAttributes[i] : ParameterAttributes.None;

            Parameters.Add(new InjectedParameterAnalysisContext(injectedParameterName, injectedParameterType, injectedParameterAttribute, i, this));
        }

        DefaultImplAttributes = defaultImplAttributes;
    }
}
