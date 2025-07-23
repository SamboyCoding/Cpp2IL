using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public interface IAction
{
    public void Apply(MethodAnalysisContext method);
}
