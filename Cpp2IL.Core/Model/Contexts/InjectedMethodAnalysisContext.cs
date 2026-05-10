using System.Collections.Generic;
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
        IEnumerable<TypeAnalysisContext> injectedParameterTypes,
        IEnumerable<string>? injectedParameterNames = null,
        IEnumerable<ParameterAttributes>? injectedParameterAttributes = null,
        MethodImplAttributes implAttributes = MethodImplAttributes.Managed) : this(parent, name, returnType, attributes, GetParameters(injectedParameterTypes, injectedParameterNames, injectedParameterAttributes), implAttributes)
    {
    }

    public InjectedMethodAnalysisContext(
        TypeAnalysisContext parent,
        string name,
        TypeAnalysisContext returnType,
        MethodAttributes attributes,
        IEnumerable<(TypeAnalysisContext Type, string? Name, ParameterAttributes Attributes)> parameters,
        MethodImplAttributes implAttributes = MethodImplAttributes.Managed) : base(null, parent)
    {
        DefaultName = name;
        DefaultReturnType = returnType;
        DefaultAttributes = attributes;

        var i = 0;
        foreach (var (parameterType, parameterName, parameterAttributes) in parameters)
        {
            Parameters.Add(new InjectedParameterAnalysisContext(parameterName, parameterType, parameterAttributes, i, this));
            i++;
        }

        DefaultImplAttributes = implAttributes;
    }

    private static IEnumerable<(TypeAnalysisContext Type, string? Name, ParameterAttributes Attributes)> GetParameters(
        IEnumerable<TypeAnalysisContext> parameterTypes,
        IEnumerable<string>? parameterNames = null,
        IEnumerable<ParameterAttributes>? parameterAttributes = null)
    {
        var typeEnumerator = parameterTypes.GetEnumerator();
        var nameEnumerator = parameterNames?.GetEnumerator();
        var attributeEnumerator = parameterAttributes?.GetEnumerator();
        if (nameEnumerator != null)
        {
            if (attributeEnumerator != null)
            {
                while (typeEnumerator.MoveNext() && nameEnumerator.MoveNext() && attributeEnumerator.MoveNext())
                {
                    yield return (typeEnumerator.Current, nameEnumerator.Current, attributeEnumerator.Current);
                }
            }
            else
            {
                while (typeEnumerator.MoveNext() && nameEnumerator.MoveNext())
                {
                    yield return (typeEnumerator.Current, nameEnumerator.Current, ParameterAttributes.None);
                }
            }
        }
        else
        {
            if (attributeEnumerator != null)
            {
                while (typeEnumerator.MoveNext() && attributeEnumerator.MoveNext())
                {
                    yield return (typeEnumerator.Current, null, attributeEnumerator.Current);
                }
            }
            else
            {
                while (typeEnumerator.MoveNext())
                {
                    yield return (typeEnumerator.Current, null, ParameterAttributes.None);
                }
            }
        }
    }
}
