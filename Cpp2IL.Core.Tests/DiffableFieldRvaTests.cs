using System.IO;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.OutputFormats;

namespace Cpp2IL.Core.Tests;

/// <summary>
/// Regression tests for the DiffableCs rendering of field-RVA default data as a REAL C# array
/// initializer on the field declaration line (<c>name = new byte[] { 0x.., ... };</c>, 16 bytes/line; an
/// ascending little-endian int32 offset table as <c>= new int[]</c>) added to
/// <see cref="DiffableCsOutputFormat"/>.
/// arrays exercise the byte[] path.
/// </summary>
public class DiffableFieldRvaTests
{
    private ApplicationAnalysisContext _ctx = null!;
    private string _outDir = null!;

    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        _ctx = TestGameLoader.LoadSimple2022Game();
        _outDir = Directory.CreateTempSubdirectory("diffable_rva_test_").FullName;
        new DiffableCsOutputFormat().DoOutput(_ctx, _outDir);
    }

    [TearDown]
    public void Cleanup() { try { Directory.Delete(_outDir, true); } catch { /* best-effort temp cleanup */ } }

    private static bool HasFieldRva(FieldAnalysisContext f) => (f.Attributes & FieldAttributes.HasFieldRVA) != 0;

    private System.Collections.Generic.List<FieldAnalysisContext> FieldRvaFields()
        => _ctx.Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Fields)
            .Where(f => HasFieldRva(f) && f.BackingData?.Field.StaticArrayInitialValue is { Length: > 0 })
            .ToList();

    private string AllOutput()
        => string.Concat(Directory.EnumerateFiles(_outDir, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    /// <summary>Every field carrying field-RVA bytes renders a real initializer on its declaration line —
    /// <c>name = new byte[]</c> OR (when the bytes are an ascending little-endian int32 offset table)
    /// <c>name = new int[]</c> — carrying the "Has Field RVA" marker, and NOT as a leading-<c>//</c> comment. The
    /// rendering is tied to the field name/data from the model, not to hard-coded fixture bytes.</summary>
    [Test]
    public void RendersFieldRvaBytesAsTypedArrayLiteral()
    {
        var rvaFields = FieldRvaFields();
        Assert.That(rvaFields, Is.Not.Empty, "fixture should contain fields with field-RVA default data");

        // Split into physical lines so we can assert the initializer is CODE (on the field line), not a comment.
        var lines = string.Concat(Directory.EnumerateFiles(_outDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)).Split('\n');

        foreach (var f in rvaFields)
        {
            var declLine = lines.FirstOrDefault(l =>
                l.Contains(" " + f.Name + " = new byte[]") || l.Contains(" " + f.Name + " = new int[]"));
            Assert.That(declLine, Is.Not.Null,
                $"{f.Name}: expected a `= new byte[]`/`= new int[]` initializer on its field declaration line");
            Assert.That(declLine!.TrimStart(), Does.Not.StartWith("//"),
                $"{f.Name}: the field-RVA initializer must be code, not a comment");
            Assert.That(declLine, Does.Contain("Has Field RVA (address hidden for diffability)"),
                $"{f.Name}: the field-RVA line should keep the address-hidden marker");
        }
    }

    /// <summary>The Simple_2022 fixture exercises BOTH rendering paths: at least one field-RVA field is a raw
    /// <c>byte[]</c> and at least one is an ascending-int32 <c>int[]</c> offset table.</summary>
    [Test]
    public void ExercisesBothByteArrayAndIntArrayPaths()
    {
        var output = AllOutput();
        Assert.That(output, Does.Contain("= new byte[]"), "byte[] literal path should be exercised");
        Assert.That(output, Does.Contain("= new int[]"), "ascending-int32 int[] literal path should be exercised");
    }

    /// <summary>The rendered hex bytes are the field's ACTUAL restored default data (spot-checked on the first
    /// row of one field), not a placeholder — the literal is `0xAA, 0xBB, ...` (16/line).</summary>
    [Test]
    public void RenderedHexBytesMatchTheDefaultData()
    {
        var f = FieldRvaFields().First(x => x.BackingData!.Field.StaticArrayInitialValue.Length >= 4);
        var data = f.BackingData!.Field.StaticArrayInitialValue;
        var firstRow = string.Join(", ", data.Take(4).Select(b => "0x" + b.ToString("X2")));

        Assert.That(AllOutput(), Does.Contain(firstRow),
            $"{f.Name}: its first bytes ({firstRow}) should appear verbatim in the byte[] literal");
    }
}
