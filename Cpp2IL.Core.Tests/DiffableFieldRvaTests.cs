using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.OutputFormats;

namespace Cpp2IL.Core.Tests;

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
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_outDir, true);
        }
        catch
        {
            //best effort
        }
    }

    private static bool HasFieldRva(FieldAnalysisContext f) => (f.Attributes & FieldAttributes.HasFieldRVA) != 0;

    private List<FieldAnalysisContext> FieldRvaFields()
        => _ctx.Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Fields)
            .Where(f => HasFieldRva(f) && f.StaticArrayInitialValue.Length > 0)
            .ToList();

    private string AllOutput()
        => string.Concat(Directory.EnumerateFiles(_outDir, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

    [Test]
    public void RendersFieldRvaBytesAsTypedArrayLiteral()
    {
        var rvaFields = FieldRvaFields();
        Assert.That(rvaFields, Is.Not.Empty, "fixture should contain fields with field-RVA default data");

        var lines = AllOutput().Split('\n');

        foreach (var f in rvaFields)
        {
            var declLine = lines.FirstOrDefault(l =>
                l.Contains(" " + f.Name + " = new byte[]") || l.Contains(" " + f.Name + " = new int[]"));
            Assert.That(declLine, Is.Not.Null,
                $"{f.Name}: expected a new byte[]/new int[] initializer on its field declaration line");
            Assert.That(declLine!.TrimStart(), Does.Not.StartWith("//"),
                $"{f.Name}: the field-RVA initializer must be code, not a comment");
            Assert.That(declLine, Does.Contain("Has Field RVA (address hidden for diffability)"),
                $"{f.Name}: the field-RVA line should keep the address-hidden marker");
        }
    }

    [Test]
    public void ExercisesBothByteArrayAndIntArrayPaths()
    {
        var output = AllOutput();
        Assert.That(output, Does.Contain("= new byte[]"), "byte[] literal path should be exercised");
        Assert.That(output, Does.Contain("= new int[]"), "int[] offset table path should be exercised");
    }

    [Test]
    public void RenderedHexBytesMatchTheDefaultData()
    {
        var f = FieldRvaFields().First(x => x.StaticArrayInitialValue.Length >= 4);
        var data = f.StaticArrayInitialValue;
        var firstRow = string.Join(", ", data.Take(4).Select(b => "0x" + b.ToString("X2")));

        Assert.That(AllOutput(), Does.Contain(firstRow),
            $"{f.Name}: its first bytes ({firstRow}) should appear verbatim in the byte[] literal");
    }
}
