namespace Cpp2IL.Core.Tests;

public class Cpp2IlApiTests
{
    [Test]
    public void UnityVersionIsCorrectlyDeterminedFromGlobalGameManagers()
    {
        var version = Cpp2IlApi.DetermineUnityVersion(null, Paths.Simple2019Game.DataDirectory);
        Assert.That(version.Equals(2019, 4, 34));
    }

    [Test]
    public void RelativePluginDirectoryIsResolvedFromApplicationBaseDirectory()
    {
        var resolved = Cpp2IlApi.ResolvePluginDirectory("Plugins");
        var expected = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Plugins"));

        Assert.That(resolved, Is.EqualTo(expected));
    }

    [Test]
    public void AbsolutePluginDirectoryIsUsedAsProvided()
    {
        var pluginDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Cpp2ILPlugins"));

        Assert.That(Cpp2IlApi.ResolvePluginDirectory(pluginDirectory), Is.EqualTo(pluginDirectory));
    }
}
