using System;
using System.Collections.Generic;
using System.IO;
using AssetRipper.Primitives;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using Xunit;

namespace LibCpp2ILTests;

public class MetadataRegistrationSearchTests
{
    [Theory]
    [InlineData(false, 0xBFFFF)]
    [InlineData(false, 0xC0000)]
    [InlineData(false, 0xC0001)]
    [InlineData(false, 865187)]
    [InlineData(true, 865187)]
    public void FindsLargeTableEndingAtReaderEnd(bool is32Bit, int count)
    {
        using var fixture = new Fixture(39, is32Bit, count, count * 12);
        Assert.Equal(Fixture.Candidate, fixture.Find());
    }

    [Theory]
    [InlineData(29)]
    [InlineData(106)]
    [InlineData(108)]
    public void PreservesVersionDependentLayouts(int version)
    {
        using var fixture = new Fixture(version, false, 0xC0000, 0xC0000 * 12);
        Assert.Equal(Fixture.Candidate, fixture.Find());
        if (version == 106)
            Assert.Equal(106.1f, fixture.Metadata.MetadataVersion);
    }

    [Theory]
    [InlineData(865187, 865187 * 12 - 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 12)]
    [InlineData(2147483648L, 12)]
    public void RejectsInvalidCountsAndRanges(long count, int payloadBytes)
    {
        using var fixture = new Fixture(39, false, count, payloadBytes);
        Assert.Equal(0ul, fixture.Find());
    }

    [Theory]
    [InlineData(0ul)]
    [InlineData(0xFFFFFFul)]
    public void RejectsNullOrUnmappedTableEvenWhenEmpty(ulong pointer)
    {
        using var fixture = new Fixture(39, false, 0, 0);
        fixture.Word(9, pointer);
        Assert.Equal(0ul, fixture.Find());
    }

    [Fact]
    public void AcceptsEmptyTableWithMappedPointer()
    {
        using var fixture = new Fixture(39, false, 0, 0);
        Assert.Equal(Fixture.Candidate, fixture.Find());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void RejectsInvalidMappedOffset(long offset)
    {
        using var fixture = new Fixture(39, false, 1, 12);
        fixture.Binary.TableOffsetOverride = offset;
        Assert.Equal(0ul, fixture.Find());
    }

    [Theory]
    [InlineData(0, 0xC0000ul)]
    [InlineData(10, 6ul)]
    [InlineData(12, 6ul)]
    public void PreservesOtherCountChecks(int word, ulong value)
    {
        using var fixture = new Fixture(39, false, 865187, 865187 * 12);
        fixture.Word(word, value);
        Assert.Equal(0ul, fixture.Find());
    }

    [Fact]
    public void ContinuesPastTruncatedCandidate()
    {
        using var fixture = new Fixture(39, false, 865187, 865187 * 12);
        // Put an otherwise valid candidate first, with only one byte of payload left.
        Array.Copy(fixture.Bytes, 0x200, fixture.Bytes, 0x100, 16 * 8);
        BitConverter.GetBytes(Fixture.ImageBase + (ulong)fixture.Bytes.Length - 1)
            .CopyTo(fixture.Bytes, 0x100 + 9 * 8);
        Assert.Equal(Fixture.Candidate, fixture.Find());
    }

    private sealed class Fixture : IDisposable
    {
        public const ulong ImageBase = 0x10000;
        public const ulong Candidate = ImageBase + 0x200;
        public const int PayloadOffset = 0x1000;
        public readonly byte[] Bytes;
        public readonly Il2CppMetadata Metadata;
        public readonly TestBinary Binary;
        private readonly int pointerSize;

        public Fixture(int version, bool is32Bit, long count, int payloadBytes)
        {
            // Empty metadata sections, except a single interface-offset record needed
            // by the v38+ parser to derive the binary type-index width.
            var metadataBytes = new byte[2048];
            using (var writer = new BinaryWriter(new MemoryStream(metadataBytes)))
            {
                writer.Write(Il2CppMetadata.MetadataMagic);
                writer.Write(version);
                // The parser probes a first record even for empty sections.
                // Point those probes at zeroed data, not at the metadata magic.
                for (var section = 0; section < 45; section++)
                {
                    writer.BaseStream.Position = 8 + section * (version >= 38 ? 12 : 8);
                    writer.Write(1024);
                }
                if (version >= 38)
                {
                    writer.BaseStream.Position = 8 + 18 * 12;
                    writer.Write(1024);
                    writer.Write(5); // One byte type index plus int32 offset.
                    writer.Write(1);
                }
            }
            Metadata = Il2CppMetadata.ReadFrom(metadataBytes, UnityVersion.Parse("6000.6.0a6"));
            // Search only needs the definition count; no type contents are consumed.
            Metadata.typeDefs = new Il2CppTypeDefinition[7];
            Bytes = new byte[PayloadOffset + payloadBytes];
            pointerSize = is32Bit ? 4 : 8;
            var words = new List<ulong> { 1, ImageBase + 0x800, 1, ImageBase + 0x800 };
            if (version < 108)
                words.AddRange(new ulong[] { 1, ImageBase + 0x800 });
            words.AddRange(new ulong[] { 8, ImageBase + 0x800 });
            if (version < 108)
                words.AddRange(new[] { unchecked((ulong)count), ImageBase + PayloadOffset });
            words.AddRange(new ulong[] { 7, ImageBase + 0x800, 7, ImageBase + 0x800, 0, 0 });
            if (Metadata.MetadataVersion >= 106.1f)
                words.AddRange(new ulong[] { 0, 0 });
            for (var i = 0; i < words.Count; i++)
                Word(i, words[i]);
            Binary = new TestBinary(Bytes, Metadata.MetadataVersion, is32Bit);
        }

        public void Word(int index, ulong value)
        {
            var bytes = pointerSize == 4 ? BitConverter.GetBytes(unchecked((uint)value)) : BitConverter.GetBytes(value);
            bytes.CopyTo(Bytes, 0x200 + index * pointerSize);
        }

        public ulong Find() => new BinarySearcher(Binary, Metadata, 0, Metadata.TypeDefinitionCount).FindMetadataRegistrationPost24_5();

        public void Dispose()
        {
            Binary.Reader.Dispose();
            Binary.Dispose();
            Metadata.Dispose();
        }
    }

    private sealed class TestReader(Stream stream, float version) : ClassReadingBinaryReader(stream)
    {
        public override float MetadataVersion => version;
    }

    private sealed class TestBinary : Il2CppBinary
    {
        private readonly byte[] bytes;
        public long? TableOffsetOverride;
        public override ClassReadingBinaryReader Reader { get; }
        // Deliberately different: range validation must use the effective Reader.
        public override long RawLength => 1;

        public TestBinary(byte[] bytes, float version, bool is32Bit) : base(new MemoryStream())
        {
            this.bytes = bytes;
            this.is32Bit = is32Bit;
            Reader = new TestReader(new MemoryStream(bytes), version) { is32Bit = is32Bit };
        }

        public override long MapVirtualAddressToRaw(ulong address, bool throwOnError = true)
        {
            if (address == Fixture.ImageBase + Fixture.PayloadOffset && TableOffsetOverride.HasValue)
                return TableOffsetOverride.Value;
            return address >= Fixture.ImageBase && address <= Fixture.ImageBase + (ulong)bytes.Length
                ? (long)(address - Fixture.ImageBase) : -1;
        }

        public override ulong MapRawAddressToVirtual(uint offset, bool throwOnError = true) => Fixture.ImageBase + offset;
        public override byte GetByteAtRawAddress(ulong address) => bytes[(int)address];
        public override ulong GetRva(ulong pointer) => pointer - Fixture.ImageBase;
        public override ReadOnlySpan<byte> GetRawBinaryContent() => bytes;
        public override ulong GetVirtualAddressOfExportedFunctionByName(string name) => 0;
        public override ReadOnlySpan<byte> GetEntirePrimaryExecutableSection() => [];
        public override ulong GetVirtualAddressOfPrimaryExecutableSection() => 0;
    }
}
