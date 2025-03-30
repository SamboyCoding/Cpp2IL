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

        var @enum = mscorlib.GetTypeByFullName("System.Enum")!;
        var list = mscorlib.GetTypeByFullName("System.Collections.Generic.List`1")!;
        var iList = mscorlib.GetTypeByFullName("System.Collections.IList")!;
        var ordinalComparer = mscorlib.GetTypeByFullName("System.OrdinalComparer")!;
        using (Assert.EnterMultipleScope())
        {
            // Simple override
            Assert.That(@enum.GetMethod("ToString", 0).BaseMethod, Is.Not.Null);
            Assert.That(@enum.GetMethod("ToString", 0).Overrides.Count(), Is.EqualTo(1));

            // Simple interface implementation
            Assert.That(list.GetMethod("get_Count").BaseMethod, Is.Null);
            Assert.That(list.GetMethod("get_Count").Overrides.Count(), Is.EqualTo(3)); // ICollection, ICollection<T>, IReadOnlyCollection<T>

            // Explicit interface implementation
            Assert.That(list.GetMethod("System.Collections.Generic.ICollection<T>.get_IsReadOnly").Overrides.Count(), Is.EqualTo(1));

            // No override
            Assert.That(list.GetMethod("EnsureCapacity").Overrides.Count(), Is.EqualTo(0));

            // Check that the base method can be found even if higher up in the inheritance chain.
            // OrdinalComparer inherits from StringComparer, but StringComparer doesn't override GetHashCode.
            Assert.That(ordinalComparer.GetMethod("GetHashCode", 0).BaseMethod?.DeclaringType?.FullName, Is.EqualTo("System.Object"));

            // Interface methods should not override anything
            Assert.That(iList.Methods.Select(m => m.Overrides.Count()), Is.All.EqualTo(0));
        }
    }
}
