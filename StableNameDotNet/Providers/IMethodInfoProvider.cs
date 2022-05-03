using System.Collections.Generic;

namespace StableNameDotNet.Providers;

public interface IMethodInfoProvider
{
    public ITypeInfoProvider ReturnType { get; }
    public IEnumerable<IParameterInfoProvider> ParameterInfoProviders { get; }
    public string MethodName { get; }
}