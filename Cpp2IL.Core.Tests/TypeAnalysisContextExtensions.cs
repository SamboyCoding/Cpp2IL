using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

internal static class TypeAnalysisContextExtensions
{
    public static MethodAnalysisContext GetMethod(this TypeAnalysisContext type, string methodName)
    {
        return type.Methods.Single(m => m.Name == methodName);
    }

    public static MethodAnalysisContext GetMethod(this TypeAnalysisContext type, string methodName, int parameterCount)
    {
        return type.Methods.Single(m => m.Name == methodName && m.Parameters.Count == parameterCount);
    }
}
