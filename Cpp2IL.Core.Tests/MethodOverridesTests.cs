using System.Linq;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

public class MethodOverridesTests
{
    [Test]
    public void OverridesTests()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();
        var mscorlib = appContext.AssembliesByName["mscorlib"];

        var @enum = GetTypeByFullName(mscorlib, "System.Enum");
        var list = GetTypeByFullName(mscorlib, "System.Collections.Generic.List`1");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(@enum.GetMethod("ToString", 0).BaseMethod, Is.Not.Null);
            Assert.That(@enum.GetMethod("ToString", 0).Overrides.Count(), Is.EqualTo(1));
            Assert.That(list.GetMethod("get_Count").BaseMethod, Is.Null);
            Assert.That(list.GetMethod("get_Count").Overrides.Count(), Is.GreaterThan(0));
        }
    }

    private static TypeAnalysisContext GetTypeByFullName(AssemblyAnalysisContext assembly, string fullName)
    {
        return assembly.Types.FirstOrDefault(t => t.FullName == fullName) ?? throw new($"Could not find {fullName} in {assembly.CleanAssemblyName}.");
    }
}
