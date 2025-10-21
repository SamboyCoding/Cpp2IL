namespace Cpp2IL.Core.Tests;

public class TypeAnalysisContextTests
{
    [Test]
    public void InterfacesHaveNoBaseType()
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

                    Assert.That(type.DefaultBaseType, Is.Null);
                    Assert.That(type.BaseType, Is.Null);
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }

    [Test]
    public void StaticClassesHaveObjectBaseType()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        using (Assert.EnterMultipleScope())
        {
            var count = 0;
            foreach (var assembly in appContext.Assemblies)
            {
                foreach (var type in assembly.Types)
                {
                    if (!type.IsStatic)
                        continue;

                    Assert.That(type.DefaultBaseType, Is.EqualTo(appContext.SystemTypes.SystemObjectType));
                    Assert.That(type.BaseType, Is.EqualTo(appContext.SystemTypes.SystemObjectType));
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
    }
}
