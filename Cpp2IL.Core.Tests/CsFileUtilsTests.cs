using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Tests;

public class CsFileUtilsTests
{
    [Test]
    public void TypesShouldHaveCorrectKeywords()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();
        var mscorlib = appContext.SystemTypes.SystemObjectType.DeclaringAssembly;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CsFileUtils.GetKeyWordsForType(appContext.SystemTypes.SystemObjectType), Is.EqualTo("public class"));
            Assert.That(CsFileUtils.GetKeyWordsForType(appContext.SystemTypes.SystemInt32Type), Is.EqualTo("public struct"));
            Assert.That(CsFileUtils.GetKeyWordsForType(mscorlib.GetTypeByFullName("System.Array")!), Is.EqualTo("public abstract class"));
            Assert.That(CsFileUtils.GetKeyWordsForType(mscorlib.GetTypeByFullName("System.EmptyArray`1")!), Is.EqualTo("internal static class"));
            Assert.That(CsFileUtils.GetKeyWordsForType(mscorlib.GetTypeByFullName("System.IDisposable")!), Is.EqualTo("public interface"));
        }
    }
}
