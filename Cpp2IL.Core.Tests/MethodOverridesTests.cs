using System.Linq;

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
        var resourceSet = mscorlib.GetTypeByFullName("System.Resources.ResourceSet")!;
        var runtimeResourceSet = mscorlib.GetTypeByFullName("System.Resources.RuntimeResourceSet")!;
        using (Assert.EnterMultipleScope())
        {
            // Simple override
            Assert.That(@enum.GetMethod("ToString", 0).BaseMethod, Is.Not.Null);
            Assert.That(@enum.GetMethod("ToString", 0).Overrides, Is.Empty);

            // Simple interface implementation
            Assert.That(list.GetMethod("get_Count").BaseMethod, Is.Null);
            Assert.That(list.GetMethod("get_Count").Overrides, Has.Count.EqualTo(3)); // ICollection, ICollection<T>, IReadOnlyCollection<T>

            // Explicit interface implementation
            Assert.That(list.GetMethod("System.Collections.Generic.ICollection<T>.get_IsReadOnly").Overrides, Has.Count.EqualTo(1));

            // No override
            Assert.That(list.GetMethod("EnsureCapacity").Overrides, Is.Empty);

            // Check that the base method can be found even if higher up in the inheritance chain.
            // OrdinalComparer inherits from StringComparer, but StringComparer doesn't override GetHashCode.
            Assert.That(ordinalComparer.GetMethod("GetHashCode", 0).BaseMethod?.DeclaringType?.FullName, Is.EqualTo("System.Object"));

            // Interface methods should not override anything
            Assert.That(iList.Methods.Select(m => m.Overrides.Count), Is.All.EqualTo(0));

            // System.Resources.RuntimeResourceSet inherits from System.Resources.ResourceSet.
            // Both implement System.Collections.IEnumerable::GetEnumerator() explicitly.
            Assert.That(resourceSet.Methods.Any(m => m.Name == "System.Collections.IEnumerable.GetEnumerator"));
            Assert.That(runtimeResourceSet.BaseType, Is.EqualTo(resourceSet));
            Assert.That(runtimeResourceSet.GetMethod("System.Collections.IEnumerable.GetEnumerator").BaseMethod, Is.Null);
        }
    }

    [Test]
    public void InterfaceMethodsShouldNotOverrideAnything()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        using (Assert.EnterMultipleScope())
        {
            var count = 0;
            foreach (var assembly in appContext.Assemblies)
            {
                foreach (var type in assembly.Types)
                {
                    if (!type.IsInterface)
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (!method.IsVirtual && !method.IsAbstract)
                            continue;

                        if (method.IsStatic || !method.IsNewSlot)
                            continue;

                        Assert.That(method.Overrides, Is.Empty);
                        count++;
                    }
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }

    [Test]
    public void InterfaceMethodsShouldHaveNoBaseMethod()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        using (Assert.EnterMultipleScope())
        {
            var count = 0;
            foreach (var assembly in appContext.Assemblies)
            {
                foreach (var type in assembly.Types)
                {
                    if (!type.IsInterface)
                        continue;

                    foreach (var method in type.Methods)
                    {
                        Assert.That(method.BaseMethod, Is.Null);
                        count++;
                    }
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }
}
