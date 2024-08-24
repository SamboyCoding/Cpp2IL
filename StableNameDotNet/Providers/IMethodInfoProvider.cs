using System.Collections.Generic;
using System.Reflection;

namespace StableNameDotNet.Providers;

public interface IMethodInfoProvider
{
    public ITypeInfoProvider ReturnType { get; }
    public IEnumerable<IParameterInfoProvider> ParameterInfoProviders { get; }
    public string MethodName { get; }
    public MethodAttributes MethodAttributes { get; }
    public MethodSemantics MethodSemantics { get; }
}
