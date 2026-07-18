using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

public class FieldRvaTests
{
    private ApplicationAnalysisContext _ctx = null!;

    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        _ctx = TestGameLoader.LoadSimple2022Game();
    }

    private static IEnumerable<FieldAnalysisContext> AllFields(ApplicationAnalysisContext ctx)
        => ctx.Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Fields);

    private static bool HasFieldRva(FieldAnalysisContext f) => (f.Attributes & FieldAttributes.HasFieldRVA) != 0;

    [Test]
    public void ReadsFieldRvaBytesForNonStaticArrayInitValueTypeField()
    {
        var int64RvaFields = AllFields(_ctx)
            .Where(f => HasFieldRva(f) && f.FieldType?.Name == "Int64")
            .ToList();

        Assert.That(int64RvaFields, Is.Not.Empty,
            "fixture should contain Int64-typed <PrivateImplementationDetails> fields with field RVA");

        foreach (var f in int64RvaFields)
            Assert.That(f.StaticArrayInitialValue, Has.Length.EqualTo(8),
                $"{f.Name}: Int64 field-RVA should yield its 8 value bytes");
    }

    [Test]
    public void StillReadsStaticArrayInitTypeSizeFields()
    {
        var saitFields = AllFields(_ctx)
            .Where(f => f.FieldType?.Name?.StartsWith("__StaticArrayInitTypeSize=") == true)
            .ToList();

        Assert.That(saitFields, Is.Not.Empty);

        foreach (var f in saitFields)
        {
            var expectedLength = int.Parse(f.FieldType!.Name!["__StaticArrayInitTypeSize=".Length..]);
            Assert.That(f.StaticArrayInitialValue, Has.Length.EqualTo(expectedLength),
                $"{f.Name}: length should match the N in the __StaticArrayInitTypeSize=N type name");
        }
    }

    [Test]
    public void ReturnsEmptyForFieldsWithoutFieldRva()
    {
        foreach (var f in AllFields(_ctx).Where(f => !HasFieldRva(f)))
            Assert.That(f.StaticArrayInitialValue, Is.Empty,
                $"{f.Name}: a field without field RVA must yield no bytes");
    }
}
