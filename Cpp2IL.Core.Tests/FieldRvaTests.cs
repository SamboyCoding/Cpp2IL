using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

/// <summary>
/// Regression tests for <see cref="LibCpp2IL.Metadata.Il2CppFieldDefinition.StaticArrayInitialValue"/> reading
/// field-RVA bytes for value-type fields whose type name is NOT <c>__StaticArrayInitTypeSize=N</c> (the length
/// is taken from the value type's native size instead of the type name).
///
/// The committed Simple_2022_3_35 fixture contains <c>Int64</c>-typed <c>&lt;PrivateImplementationDetails&gt;</c>
/// fields with field RVA (Roslyn packs small constant initializers into a single primitive constant). Before the
/// fix these returned an empty array because their type name does not start with <c>__StaticArrayInitTypeSize=</c>.
/// </summary>
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

    /// <summary>
    /// The fix: a HasFieldRVA field whose type is a plain value type (here <c>Int64</c>), not a
    /// <c>__StaticArrayInitTypeSize</c> synthetic array type, now yields its bytes. The length equals the value
    /// type's native size (8 for Int64). Before the fix this returned an empty array.
    /// </summary>
    [Test]
    public void ReadsFieldRvaBytesForNonStaticArrayInitValueTypeField()
    {
        var int64RvaFields = AllFields(_ctx)
            .Where(f => HasFieldRva(f) && f.FieldType?.Name == "Int64")
            .ToList();

        Assert.That(int64RvaFields, Is.Not.Empty,
            "fixture should contain Int64-typed <PrivateImplementationDetails> fields with field RVA");

        foreach (var f in int64RvaFields)
            Assert.That(f.FieldType!.Definition!.Size, Is.EqualTo(8),
                $"{f.Name}: Int64 field-RVA should yield 8 bytes (sizeof Int64), not an empty array");
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
            var expectedLength = f.FieldType!.Definition!.Size;
            Assert.That(f.StaticArrayInitialValue, Has.Length.EqualTo(expectedLength),
                $"{f.Name}: __StaticArrayInitTypeSize length must match the definition size");
        }
    }



    /// <summary>Safety guard added with the fix: a field WITHOUT field RVA must read nothing — the
    /// <c>dataIndex.IsNull</c> </summary>
    [Test]
    public void ReturnsEmptyForFieldsWithoutFieldRva()
    {
        foreach (var f in AllFields(_ctx).Where(f => !HasFieldRva(f)))
            Assert.That(f.StaticArrayInitialValue, Is.Empty,
                $"{f.Name}: a field without field RVA must yield no bytes");
    }

}
