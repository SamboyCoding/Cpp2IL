using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Tests;

public class CsFileUtilsTests
{
    [TestCase(FieldAttributes.PrivateScope, "private")]
    [TestCase(FieldAttributes.Private, "private")]
    [TestCase(FieldAttributes.Public, "public")]
    [TestCase(FieldAttributes.Family, "protected")]
    [TestCase(FieldAttributes.Assembly, "internal")]
    [TestCase(FieldAttributes.FamORAssem, "protected internal")]
    [TestCase(FieldAttributes.FamANDAssem, "private protected")]
    [TestCase(FieldAttributes.PrivateScope | FieldAttributes.Static | FieldAttributes.InitOnly, "private static readonly")]
    public void FieldsShouldHaveCorrectKeywords(FieldAttributes attributes, string expected)
    {
        var appContext = TestGameLoader.LoadSimple2019Game();
        var field = new InjectedFieldAnalysisContext("value", appContext.SystemTypes.SystemInt32Type,
            attributes, appContext.SystemTypes.SystemObjectType);

        Assert.That(CsFileUtils.GetKeyWordsForField(field), Is.EqualTo(expected));
        Assert.That(field.Attributes, Is.EqualTo(attributes));
    }

    [Test]
    public void UnknownFieldVisibilityShouldStillThrow()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();
        var field = new InjectedFieldAnalysisContext("value", appContext.SystemTypes.SystemInt32Type,
            FieldAttributes.FieldAccessMask, appContext.SystemTypes.SystemObjectType);

        Assert.Throws<ArgumentOutOfRangeException>(() => CsFileUtils.GetAccessModifiers(field));
    }

    [Test]
    public void PrivateScopeFieldsShouldBeWritten()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();
        var field = appContext.SystemTypes.SystemInt32Type.Fields.First(f => !f.IsStatic);
        var attributes = field.Attributes;
        var output = Directory.CreateTempSubdirectory("diffable_visibility_").FullName;
        try
        {
            field.Visibility = FieldAttributes.PrivateScope;
            new DiffableCsOutputFormat().DoOutput(appContext, output);

            var source = File.ReadAllText(Path.Combine(output, "DiffableCs", "mscorlib", "System", "Int32.cs"));
            Assert.That(source, Does.Contain($"private int {field.Name};"));
            Assert.That(field.Visibility, Is.EqualTo(FieldAttributes.PrivateScope));
        }
        finally
        {
            field.Attributes = attributes;
            Directory.Delete(output, true);
        }
    }

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
