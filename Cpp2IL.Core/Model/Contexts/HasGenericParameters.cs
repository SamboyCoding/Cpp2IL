using System.Collections.Generic;

namespace Cpp2IL.Core.Model.Contexts;

public abstract class HasGenericParameters(uint token, ApplicationAnalysisContext appContext)
    : HasCustomAttributesAndName(token, appContext)
{
    public abstract List<GenericParameterTypeAnalysisContext> GenericParameters { get; }
}
