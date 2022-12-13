using NUnit.Framework;

namespace Cpp2IL.Core.Tests;
public class Cpp2IlApiTests
{
    [Test]
    public void UnityVersionIsCorrectlyDeterminedFromGlobalGameManagers()
    {
        var version = Cpp2IlApi.DetermineUnityVersion(null, Paths.TestFilesDirectory + "Simple_2019_4_34/Simple_2019_4_34_Data/");
        Assert.That(version.IsEqual(2019, 4, 34));
    }
}
