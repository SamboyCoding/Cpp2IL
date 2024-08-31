using NUnit.Framework;

namespace Cpp2IL.Core.Tests;

public class Cpp2IlApiTests
{
    [Test]
    public void UnityVersionIsCorrectlyDeterminedFromGlobalGameManagers()
    {
        var version = Cpp2IlApi.DetermineUnityVersion(null, Paths.Simple2019Game.DataDirectory);
        Assert.That(version.Equals(2019, 4, 34));
    }
}
