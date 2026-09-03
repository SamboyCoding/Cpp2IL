using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AssetRipper.Primitives;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;

[assembly:RegisterCpp2IlPlugin(typeof(Cpp2IL.Plugin.Mfuscator.MfuscatorSupportPlugin))]

namespace Cpp2IL.Plugin.Mfuscator;

public class MfuscatorSupportPlugin : Cpp2IlPlugin
{
    private const int MaxHeaderSize = 480; //somewhat arbitrary
    private const int MinHeaderWords = 8;
    private const int MinOffsetDelta = 0x10;
    private const int MaxOffsetDelta = 0x40;
    private const int MaxLayoutSearchResults = 65536;
    private static readonly bool[] BothSigns = [true, false];

    private enum Section
    {
        StringLiterals,
        StringLiteralData,
        Strings,
        Events,
        Properties,
        Methods,
        ParameterDefaultValues,
        FieldDefaultValues,
        FieldAndParameterDefaultValueData,
        FieldMarshaledSizes,
        Parameters,
        Fields,
        GenericParameters,
        GenericParameterConstraints,
        GenericContainers,
        NestedTypes,
        Interfaces,
        VtableMethods,
        InterfaceOffsets,
        TypeDefinitions,
        TypeInlineArrays,
        RgctxEntries,
        Images,
        Assemblies,
        MetadataUsageLists,
        MetadataUsagePairs,
        FieldRefs,
        ReferencedAssemblies,
        AttributesInfo,
        AttributeTypes,
        AttributeData,
        AttributeDataRange,
        UnresolvedVirtualCallParameterTypes,
        UnresolvedVirtualCallParameterRanges,
        WindowsRuntimeTypeNames,
        WindowsRuntimeStrings,
        ExportedTypeDefinitions,
        MethodSpecsOnGenericType,
        GenericMethodSpecsOnType,
        MethodSpecs,
        GenericMethodFunctionsDefinitions,
        GenericMethodFunctionsDefinitionsWithAdjustor,
        InvokerIndices,
        RgctxRanges,
        RgctxValues,
        StaticConstructorTypeIndices,
    }

    private sealed class MetadataLayout
    {
        public readonly byte Version;
        public readonly int BytesPerSectionHeaderField;
        public readonly Section[] Sections;
        public readonly Dictionary<Section, int> RecordSizes;

        public int SectionCount => Sections.Length;
        public int OriginalHeaderSize => 8 + SectionCount * BytesPerSectionHeaderField; //magic + version + one field per section
        public int IndexOf(Section section) => Array.IndexOf(Sections, section);

        public MetadataLayout(byte version, UnityVersion unityVersion)
        {
            Version = version;
            BytesPerSectionHeaderField = version >= 38 ? 12 : 8;

            List<Section> sections =
            [
                Section.StringLiterals, Section.StringLiteralData, Section.Strings, Section.Events, Section.Properties, Section.Methods,
                Section.ParameterDefaultValues, Section.FieldDefaultValues, Section.FieldAndParameterDefaultValueData, Section.FieldMarshaledSizes,
                Section.Parameters, Section.Fields, Section.GenericParameters, Section.GenericParameterConstraints, Section.GenericContainers,
                Section.NestedTypes, Section.Interfaces, Section.VtableMethods, Section.InterfaceOffsets, Section.TypeDefinitions,
            ];

            if (version >= 104)
                sections.Add(Section.TypeInlineArrays);

            if (version == 24 && unityVersion.LessThan(2019))
                sections.Add(Section.RgctxEntries); //pre-24.2

            sections.AddRange([Section.Images, Section.Assemblies]);

            if (version <= 24)
                sections.AddRange([Section.MetadataUsageLists, Section.MetadataUsagePairs]); //moved to the binary in v27

            sections.AddRange([Section.FieldRefs, Section.ReferencedAssemblies]);

            if (version >= 29)
                sections.AddRange([Section.AttributeData, Section.AttributeDataRange]);
            else
                sections.AddRange([Section.AttributesInfo, Section.AttributeTypes]);

            sections.AddRange([Section.UnresolvedVirtualCallParameterTypes, Section.UnresolvedVirtualCallParameterRanges, Section.WindowsRuntimeTypeNames]);

            if (version >= 27)
                sections.Add(Section.WindowsRuntimeStrings);

            if (version >= 24)
                sections.Add(Section.ExportedTypeDefinitions);

            if (version >= 108)
                sections.AddRange([Section.MethodSpecsOnGenericType, Section.GenericMethodSpecsOnType, Section.MethodSpecs, Section.GenericMethodFunctionsDefinitions, Section.GenericMethodFunctionsDefinitionsWithAdjustor, Section.InvokerIndices, Section.RgctxRanges, Section.RgctxValues, Section.StaticConstructorTypeIndices]);

            Sections = sections.ToArray();

            RecordSizes = new Dictionary<Section, int>
            {
                { Section.StringLiterals, version >= 35 ? 4 : 8 }, //Il2CppStringLiteral, loses its length in v35
                { Section.Events, 24 }, //Il2CppEventDefinition
                { Section.Properties, 20 }, //Il2CppPropertyDefinition
                { Section.Methods, version >= 31 ? 36 : 32 }, //Il2CppMethodDefinition, gains returnParameterToken in v31
                { Section.ParameterDefaultValues, 12 }, //Il2CppParameterDefaultValue
                { Section.FieldDefaultValues, 12 }, //Il2CppFieldDefaultValue
                { Section.FieldMarshaledSizes, 12 }, //Il2CppFieldMarshaledSize
                { Section.Parameters, 12 }, //Il2CppParameterDefinition
                { Section.Fields, 12 }, //Il2CppFieldDefinition
                { Section.GenericParameters, 16 }, //Il2CppGenericParameter
                { Section.GenericParameterConstraints, 4 }, //TypeIndex
                { Section.GenericContainers, 16 }, //Il2CppGenericContainer
                { Section.NestedTypes, 4 }, //TypeDefinitionIndex
                { Section.Interfaces, 4 }, //TypeIndex
                { Section.VtableMethods, 4 }, //EncodedMethodIndex
                { Section.InterfaceOffsets, 8 }, //Il2CppInterfaceOffsetPair
                { Section.TypeDefinitions, version >= 35 ? 84 : 88 }, //Il2CppTypeDefinition, loses elementTypeIndex in v35
                { Section.Images, 40 }, //Il2CppImageDefinition
                { Section.Assemblies, 64 }, //Il2CppAssemblyDefinition
                { Section.FieldRefs, 8 }, //Il2CppFieldRef
                { Section.ReferencedAssemblies, 4 }, //int32
                { Section.AttributesInfo, 12 }, //Il2CppCustomAttributeTypeRange
                { Section.AttributeTypes, 4 }, //TypeIndex
                { Section.AttributeDataRange, 8 }, //Il2CppCustomAttributeDataRange
                { Section.UnresolvedVirtualCallParameterTypes, 4 }, //TypeIndex
                { Section.UnresolvedVirtualCallParameterRanges, 8 }, //Il2CppRange
                { Section.WindowsRuntimeTypeNames, 8 }, //Il2CppWindowsRuntimeTypeNamePair
                { Section.ExportedTypeDefinitions, 4 }, //TypeDefinitionIndex
            };
        }
    }

    private delegate bool SectionValidator(ReadOnlySpan<byte> decrypted);

    private record struct ReconstructedSection(int OffsetAccordingToHeader, int Length, int Delta)
    {
        public int ActualOffset => OffsetAccordingToHeader + Delta;
    }

    private class SectionRangeComparer : IEqualityComparer<(int Start, int End)[]>
    {
        public bool Equals((int Start, int End)[]? x, (int Start, int End)[]? y)
        {
            return x != null && y != null && x.SequenceEqual(y);
        }

        public int GetHashCode((int Start, int End)[] obj)
        {
            return obj.Aggregate(0, (hash, range) => HashCode.Combine(hash, range.Start, range.End));
        }
    }

    public override string Name => "Mfuscator Support"; //more like midfuscator amirite
    public override string Description => "Supports loading metadata files which have been mangled by mfuscator.";

    public override void OnLoad()
    {
        RegisterMetadataFixupFunc(TryFixupMfuscatorMetadata);
    }

    private static byte HeaderKeyByte(int xorKey, bool isPlus, int position) => (byte)(isPlus ? xorKey + position : xorKey - position);

    private static void CyclicXorHeader(ReadOnlySpan<byte> data, Span<byte> output, byte xorKey, bool isPlus)
    {
        for (var i = 0; i < data.Length; i++)
            output[i] = (byte)(data[i] ^ HeaderKeyByte(xorKey, isPlus, i));
    }

    //minus mode is -(base - i), which is just (-base) + i
    private static void CyclicXor(ReadOnlySpan<byte> data, Span<byte> output, byte startKey)
    {
        for (var i = 0; i < data.Length; i++)
            output[i] = (byte)(data[i] ^ (byte)(startKey + i));
    }

    private static bool TryDeriveHeaderKey(ReadOnlySpan<byte> data, out byte xorKey, out bool isPlus, out int runWords)
    {
        xorKey = 0;
        isPlus = false;
        runWords = 0;

        var fileSize = (uint)data.Length;
        var maxWords = Math.Min(MaxHeaderSize, data.Length) / 4;
        var waysToGetBestRun = 0;

        for (var key = 0; key < 256; key++)
        {
            foreach (var plus in BothSigns)
            {
                var run = 0;
                while (run < maxWords)
                {
                    var offset = run * 4;
                    uint word = 0;
                    for (var i = 0; i < 4; i++)
                        word |= (uint)(data[offset + i] ^ HeaderKeyByte(key, plus, offset + i)) << (8 * i);

                    if (word >= fileSize)
                        break;

                    run++;
                }

                if (run > runWords)
                {
                    runWords = run;
                    xorKey = (byte)key;
                    isPlus = plus;
                    waysToGetBestRun = 1;
                }
                else if (run == runWords)
                    waysToGetBestRun++;
            }
        }

        return runWords >= MinHeaderWords && waysToGetBestRun == 1;
    }

    //The first string literal record is {length, 0}, so bytes 1 through 7 of the section are zero in the clear and the raw bytes there are the key stream itself
    private static bool LooksLikeStringLiteralsStart(ReadOnlySpan<byte> data, int pos)
    {
        if (pos + 8 > data.Length)
            return false;

        for (var i = 1; i < 7; i++)
        {
            if ((byte)(data[pos + i] + 1) != data[pos + i + 1])
                return false;
        }

        return true;
    }

    //The key run usually stops exactly where the string literals begin, but the first literal record can land under the file size by chance
    private int FindHeaderEnd(ReadOnlySpan<byte> data, int runEnd)
    {
        var best = -1;
        for (var pos = MinHeaderWords * 4; pos <= MaxHeaderSize; pos += 4)
        {
            if (LooksLikeStringLiteralsStart(data, pos) && (best < 0 || Math.Abs(pos - runEnd) < Math.Abs(best - runEnd)))
                best = pos;
        }

        if (best < 0)
            throw new Exception("Failed to determine header size, couldn't find the start of the string literals");

        if (best != runEnd)
            Logger.VerboseNewline($"Header key run ends at {runEnd} but the string literals start at {best}, using the latter as the header size.");

        return best;
    }

    private static Dictionary<int, long>? ExpectedLengthsFromTypeDefinitions(byte[] data, int start, int length, MetadataLayout layout)
    {
        if ((long)start + length > data.Length)
            return null;

        var recordSize = layout.RecordSizes[Section.TypeDefinitions];
        Section[] sectionForCount = [Section.Methods, Section.Properties, Section.Fields, Section.Events, Section.NestedTypes, Section.VtableMethods, Section.Interfaces, Section.InterfaceOffsets];
        Span<long> sums = stackalloc long[sectionForCount.Length];

        for (var pos = start; pos + recordSize <= start + length; pos += recordSize)
        {
            var counts = data.AsSpan(pos + recordSize - 24, 16);
            for (var i = 0; i < sums.Length; i++)
                sums[i] += BinaryPrimitives.ReadUInt16LittleEndian(counts[(i * 2)..]);
        }

        var expected = new Dictionary<int, long>();
        for (var i = 0; i < sums.Length; i++)
            expected[layout.IndexOf(sectionForCount[i])] = sums[i] * layout.RecordSizes[sectionForCount[i]];

        return expected;
    }

    private static Dictionary<int, long>? ExpectedLengthsFromImages(byte[] data, int start, int length, MetadataLayout layout)
    {
        if ((long)start + length > data.Length)
            return null;

        var recordSize = layout.RecordSizes[Section.Images];
        var imageCount = length / recordSize;
        long types = 0, exportedTypes = 0, customAttributes = 0;
        for (var i = 0; i < imageCount; i++)
        {
            var image = data.AsSpan(start + i * recordSize, recordSize);
            types += BinaryPrimitives.ReadUInt32LittleEndian(image[12..]);
            exportedTypes += BinaryPrimitives.ReadUInt32LittleEndian(image[20..]);
            customAttributes += BinaryPrimitives.ReadUInt32LittleEndian(image[36..]);
        }

        var expected = new Dictionary<int, long>
        {
            { layout.IndexOf(Section.TypeDefinitions), types * layout.RecordSizes[Section.TypeDefinitions] },
            { layout.IndexOf(Section.Assemblies), (long)imageCount * layout.RecordSizes[Section.Assemblies] },
            { layout.IndexOf(Section.ExportedTypeDefinitions), exportedTypes * layout.RecordSizes[Section.ExportedTypeDefinitions] },
        };

        if (layout.IndexOf(Section.AttributeDataRange) is var attributeDataRangeIndex && attributeDataRangeIndex >= 0)
            expected[attributeDataRangeIndex] = (customAttributes + 1) * layout.RecordSizes[Section.AttributeDataRange]; //one extra range terminates the table

        return expected;
    }

    //Custom attribute ranges are sorted by data offset, start at zero, and the final range points at the end of the attribute data (give or take the padding to four bytes)
    private static bool AttributeDataRangesAgree(byte[] data, List<ReconstructedSection> sections, MetadataLayout layout)
    {
        var ranges = sections[layout.IndexOf(Section.AttributeDataRange)];
        var attributeDataLength = sections[layout.IndexOf(Section.AttributeData)].Length;
        if (ranges.Length < 8 || (long)ranges.ActualOffset + ranges.Length > data.Length)
            return false;

        long previous = 0;
        for (var pos = ranges.ActualOffset; pos < ranges.ActualOffset + ranges.Length; pos += 8)
        {
            var startOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            if (startOffset < previous || (pos == ranges.ActualOffset && startOffset != 0))
                return false;

            previous = startOffset;
        }

        return previous <= attributeDataLength && attributeDataLength <= previous + 3;
    }

    //Unresolved virtual call parameter ranges tile the parameter type list exactly
    private static bool VirtualCallRangesAgree(byte[] data, List<ReconstructedSection> sections, MetadataLayout layout)
    {
        var ranges = sections[layout.IndexOf(Section.UnresolvedVirtualCallParameterRanges)];
        var typeCount = sections[layout.IndexOf(Section.UnresolvedVirtualCallParameterTypes)].Length / layout.RecordSizes[Section.UnresolvedVirtualCallParameterTypes];
        if ((long)ranges.ActualOffset + ranges.Length > data.Length)
            return false;

        var next = 0;
        for (var pos = ranges.ActualOffset; pos < ranges.ActualOffset + ranges.Length; pos += 8)
        {
            var start = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
            if (start != next || length < 0)
                return false;

            next += length;
        }

        return next == typeCount;
    }

    private List<(int Start, int End)[]> FindSectionLayouts(byte[] data, uint[] headerWords, int dataStart, MetadataLayout layout)
    {
        var recordSizes = layout.Sections.Select(section => layout.RecordSizes.GetValueOrDefault(section)).ToArray();
        var alignBefore = new Dictionary<int, int> { { layout.IndexOf(Section.FieldAndParameterDefaultValueData), 8 } };
        var maxAlignPad = alignBefore.Values.Max() - 1;
        var maxEnd = data.Length;

        var typeDefinitionsIndex = layout.IndexOf(Section.TypeDefinitions);
        var imagesIndex = layout.IndexOf(Section.Images);
        var assembliesIndex = layout.IndexOf(Section.Assemblies);
        var attributeDataRangeIndex = layout.IndexOf(Section.AttributeDataRange);
        var virtualCallRangesIndex = layout.IndexOf(Section.UnresolvedVirtualCallParameterRanges);
        var firstSectionAllowedEmpty = layout.IndexOf(Section.UnresolvedVirtualCallParameterTypes); //this cuts down on the number of invalid paths we get quite significantly

        var pool = headerWords.ToArray();
        Array.Sort(pool);
        var used = new bool[pool.Length];
        var sections = new List<ReconstructedSection>();
        var typeDefinitionExpectations = new Dictionary<(int, int), Dictionary<int, long>?>();
        var imageExpectations = new Dictionary<(int, int), Dictionary<int, long>?>();
        var attributeRangeVerdicts = new Dictionary<(int, int), bool>();
        var virtualCallRangeVerdicts = new Dictionary<(int, int), bool>();
        var distinctLayouts = new HashSet<(int Start, int End)[]>(new SectionRangeComparer());
        var results = new List<(int Start, int End)[]>();
        var chainCount = 0;

        DepthFirstSearch(dataStart, new Dictionary<int, long>());

        Logger.VerboseNewline($"Walked {chainCount} chains through the header, giving {results.Count} distinct section layouts.");

        //Decoy lengths are always (confirm?) a little larger than the real ones, so where layouts still disagree the shortest one is right
        return [.. results.OrderBy(result => result[^1].End)];

        int LastAtMost(long value)
        {
            int lo = 0, hi = pool.Length - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (pool[mid] <= value)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }

            return found;
        }

        int FirstAtLeast(long value)
        {
            int lo = 0, hi = pool.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (pool[mid] < value)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }

        //Alignment is according to the header before it was mangled, i.e. with original header size
        int ApplyAlignment(int actualPos, int sectionIndex)
        {
            if (!alignBefore.TryGetValue(sectionIndex, out var align))
                return actualPos;

            var originalOffset = layout.OriginalHeaderSize + (actualPos - dataStart);
            var remainder = originalOffset % align;
            return remainder == 0 ? actualPos : actualPos + align - remainder;
        }

        bool PlacedSectionsMatch(Dictionary<int, long>? expected) => expected != null && expected.All(pair => sections[pair.Key].Length == pair.Value);

        bool Cached(Dictionary<(int, int), bool> verdicts, int sectionIndex, Func<byte[], List<ReconstructedSection>, MetadataLayout, bool> check)
        {
            var key = (sections[sectionIndex].ActualOffset, sections[sectionIndex].Length);
            if (!verdicts.TryGetValue(key, out var verdict))
                verdict = verdicts[key] = check(data, sections, layout);

            return verdict;
        }

        void DepthFirstSearch(int actualPos, Dictionary<int, long> expectedLengths)
        {
            if (chainCount >= MaxLayoutSearchResults)
                return;

            var sectionIndex = sections.Count;
            actualPos = ApplyAlignment(actualPos, sectionIndex);

            if (sectionIndex == layout.SectionCount)
            {
                chainCount++;
                var result = sections.Select(section => (section.ActualOffset, section.ActualOffset + section.Length)).ToArray();
                if (distinctLayouts.Add(result))
                    results.Add(result);

                return;
            }

            if (actualPos >= maxEnd)
                return;

            if (attributeDataRangeIndex >= 0 && sectionIndex == attributeDataRangeIndex + 1 && !Cached(attributeRangeVerdicts, attributeDataRangeIndex, AttributeDataRangesAgree))
                return;

            if (sectionIndex == virtualCallRangesIndex + 1 && !Cached(virtualCallRangeVerdicts, virtualCallRangesIndex, VirtualCallRangesAgree))
                return;

            if (sectionIndex == typeDefinitionsIndex + 1)
            {
                var typeDefinitions = sections[typeDefinitionsIndex];
                var key = (typeDefinitions.ActualOffset, typeDefinitions.Length);
                if (!typeDefinitionExpectations.TryGetValue(key, out var expected))
                    expected = typeDefinitionExpectations[key] = ExpectedLengthsFromTypeDefinitions(data, typeDefinitions.ActualOffset, typeDefinitions.Length, layout);

                if (!PlacedSectionsMatch(expected))
                    return;
            }
            else if (sectionIndex == imagesIndex + 1)
            {
                var images = sections[imagesIndex];
                var key = (images.ActualOffset, images.Length);
                if (!imageExpectations.TryGetValue(key, out var expected))
                    expected = imageExpectations[key] = ExpectedLengthsFromImages(data, images.ActualOffset, images.Length, layout);

                if (expected == null || expected[typeDefinitionsIndex] != sections[typeDefinitionsIndex].Length)
                    return;

                expectedLengths = expected;
            }

            var beforeFrom = FirstAtLeast((long)actualPos - MaxOffsetDelta);
            var beforeTo = LastAtMost((long)actualPos - MinOffsetDelta);
            var afterFrom = FirstAtLeast((long)actualPos + MinOffsetDelta);
            var afterTo = LastAtMost((long)actualPos + MaxOffsetDelta);

            var lengthLimit = LastAtMost((long)maxEnd + maxAlignPad - actualPos);
            var hasExpectedLength = expectedLengths.TryGetValue(sectionIndex, out var expectedLength);
            var recordSize = recordSizes[sectionIndex];

            for (var window = 0; window < 2; window++)
            {
                var from = window == 0 ? beforeFrom : afterFrom;
                var to = window == 0 ? beforeTo : afterTo;

                for (var i = from; i <= to; i++)
                {
                    //Equal words are interchangeable, so only the first free one of each run is worth trying
                    if (used[i] || (i > from && pool[i] == pool[i - 1] && !used[i - 1]))
                        continue;

                    var candidateOffset = pool[i];
                    var delta = actualPos - candidateOffset;
                    used[i] = true;

                    for (var j = lengthLimit; j >= 0; j--)
                    {
                        if (used[j] || (j < lengthLimit && pool[j] == pool[j + 1] && !used[j + 1]))
                            continue;

                        var length = pool[j];

                        if (hasExpectedLength)
                        {
                            if (length != expectedLength)
                                continue;
                        }
                        else if (sectionIndex < firstSectionAllowedEmpty && length == 0 || recordSize != 0 && length % recordSize != 0)
                            continue;

                        used[j] = true;
                        sections.Add(new ReconstructedSection((int)candidateOffset, (int)length, (int)delta));

                        DepthFirstSearch((int)(actualPos + length), expectedLengths);

                        sections.RemoveAt(sections.Count - 1);
                        used[j] = false;
                    }

                    used[i] = false;
                }
            }
        }
    }

    private static bool LooksLikeStrings(ReadOnlySpan<byte> decrypted)
    {
        var sawTerminator = false;
        foreach (var b in decrypted)
        {
            if (b == 0)
                sawTerminator = true;
            else if (b < 0x20 || b == 0x7F)
                return false;
        }

        return sawTerminator;
    }

    private Dictionary<int, byte[]> DecryptEncryptedSections(byte[] encryptedMetadata, (int Start, int End)[] sections, byte stringLiteralsXorKey, MetadataLayout layout)
    {
        var methodTokenOffset = layout.Version >= 31 ? 24 : 20; //returnParameterToken lands before the token in v31
        (Section Section, int ProbeLength, SectionValidator IsValid)[] validators =
        [
            (Section.StringLiterals, 8, probe => probe[1..].IndexOfAnyExcept((byte)0) < 0),
            (Section.StringLiteralData, 2, probe => probe[0] == 0 && probe[1] == 0),
            (Section.Strings, 32, LooksLikeStrings),
            (Section.Properties, 20, probe => BinaryPrimitives.ReadUInt32LittleEndian(probe[16..]) == 0x17000001),
            (Section.Methods, methodTokenOffset + 4, probe => BinaryPrimitives.ReadUInt32LittleEndian(probe[methodTokenOffset..]) == 0x06000001),
            (Section.Fields, 12, probe => BinaryPrimitives.ReadUInt32LittleEndian(probe[8..]) == 0x04000001),
            (Section.Assemblies, 8, probe => BinaryPrimitives.ReadUInt32LittleEndian(probe) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(probe[4..]) == 0x20000001),
        ];

        Span<byte> probe = stackalloc byte[validators.Max(validator => validator.ProbeLength)];
        var literals = sections[layout.IndexOf(Section.StringLiterals)];

        foreach (var usingOffsetNotSize in BothSigns)
        {
            foreach (var literalsIsPlus in BothSigns)
            {
                var literalsComponent = usingOffsetNotSize ? literals.Start : literals.End - literals.Start;
                var addend = (byte)((literalsIsPlus ? stringLiteralsXorKey : -stringLiteralsXorKey) - literalsComponent);

                var decryptedSectionBytes = new Dictionary<int, byte[]>();
                var allValid = true;
                foreach (var (section, probeLength, isValid) in validators)
                {
                    var index = layout.IndexOf(section);
                    var (start, end) = sections[index];
                    var size = end - start;
                    if (size < probeLength)
                    {
                        allValid = false;
                        break;
                    }

                    var component = usingOffsetNotSize ? start : size;
                    byte? startKey = null;
                    foreach (var isPlus in BothSigns)
                    {
                        var candidate = (byte)(isPlus ? addend + component : -(addend + component));
                        CyclicXor(encryptedMetadata.AsSpan(start, probeLength), probe, candidate);
                        if (isValid(probe[..probeLength]))
                        {
                            startKey = candidate;
                            break;
                        }
                    }

                    if (startKey == null)
                    {
                        allValid = false;
                        break;
                    }

                    var decryptedSection = new byte[size];
                    CyclicXor(encryptedMetadata.AsSpan(start, size), decryptedSection, startKey.Value);
                    decryptedSectionBytes[index] = decryptedSection;
                }

                if (allValid)
                {
                    Logger.VerboseNewline($"Section keys are based on {(usingOffsetNotSize ? "offsets" : "sizes")}, with addend 0x{addend:X2}. String literals use {(literalsIsPlus ? "plus" : "minus")} sign.");
                    return decryptedSectionBytes;
                }
            }
        }

        throw new Exception("Failed to decrypt sections with either offset-based or size-based keys");
    }

    private byte[] RebuildMetadata(byte[] encryptedMetadata, (int Start, int End)[] sections, byte stringLiteralsXorKey, int offsetDelta, MetadataLayout layout)
    {
        var decryptedSections = DecryptEncryptedSections(encryptedMetadata, sections, stringLiteralsXorKey, layout);

        var decryptedMetadata = new byte[encryptedMetadata.Length];
        Span<byte> magicAndVersion = [0xAF, 0x1B, 0xB1, 0xFA, layout.Version, 0x00, 0x00, 0x00];
        magicAndVersion.CopyTo(decryptedMetadata);

        var headerSpan = decryptedMetadata.AsSpan(8);

        for (var i = 0; i < sections.Length; i++)
        {
            var (start, end) = sections[i];

            BinaryPrimitives.WriteInt32LittleEndian(headerSpan, start + offsetDelta);
            BinaryPrimitives.WriteInt32LittleEndian(headerSpan[4..], end - start);
            headerSpan = headerSpan[layout.BytesPerSectionHeaderField..];

            //Decrypted if it was encrypted, else copy straight from the original file
            var sectionData = decryptedSections.GetValueOrDefault(i) ?? encryptedMetadata.AsSpan(start, end - start).ToArray();
            sectionData.CopyTo(decryptedMetadata.AsSpan(start + offsetDelta, end - start));
        }

        return decryptedMetadata;
    }

    private byte[]? TryFixupMfuscatorMetadata(byte[] originalBytes, UnityVersion unityVersion)
    {
        if (!TryDeriveHeaderKey(originalBytes, out var headerXorKey, out var headerIsPlus, out var headerRunWords))
        {
            Logger.WarnNewline("Couldn't derive a header XOR key, this metadata doesn't look like mfuscator's handiwork.");
            return null;
        }

        var headerLength = FindHeaderEnd(originalBytes, headerRunWords * 4);
        var decryptedHeader = new byte[headerLength];
        CyclicXorHeader(originalBytes.AsSpan(0, headerLength), decryptedHeader, headerXorKey, headerIsPlus);
        var headerWords = MemoryMarshal.Cast<byte, uint>(decryptedHeader).ToArray();

        var stringLiteralsXorKey = (byte)(originalBytes[headerLength + 1] - 1); //byte 1 of the first literal record is zero in the clear, so the raw byte is key + 1

        byte MetadataVersion;
        if (unityVersion.LessThan(2017))
            MetadataVersion = 23;
        else if (unityVersion.LessThan(2020, 2))
            MetadataVersion = 24;
        else if (unityVersion.LessThan(2021, 3))
            MetadataVersion = 27;
        else if (unityVersion.LessThan(2022, 3, 33))
            MetadataVersion = 29;
        else if(unityVersion.LessThan(6000, 3, 0, UnityVersionType.Alpha, 2))
            MetadataVersion = 31;
        else if(unityVersion.LessThan(6000, 3, 0, UnityVersionType.Alpha, 5))
            MetadataVersion = 35;
        else if (unityVersion.LessThan(6000, 3, 0, UnityVersionType.Beta, 1))
            MetadataVersion = 38;
        else if (unityVersion.LessThan(6000, 5, 0, UnityVersionType.Alpha, 3))
            MetadataVersion = 39;
        else if (unityVersion.LessThan(6000, 5, 0, UnityVersionType.Alpha, 5))
            MetadataVersion = 104;
        else if (unityVersion.LessThan(6000, 3, 0, UnityVersionType.Alpha, 6))
            MetadataVersion = 105;
        else
            MetadataVersion = 106;

        if (MetadataVersion < 27)
            throw new NotImplementedException("Metadata versions below 27 aren't currently supported (largely because mfuscator itself doesn't support these versions)");

        var layout = new MetadataLayout(MetadataVersion, unityVersion);

        if (layout.BytesPerSectionHeaderField == 12)
            throw new NotImplementedException("Metadata versions with 12 bytes per section header field aren't currently supported");

        Logger.InfoNewline($"Mfuscator header decrypted successfully. Header XOR key: 0x{headerXorKey:X2} ({(headerIsPlus ? "plus" : "minus")} rotation). Header length: {headerLength} bytes. String literals XOR key: 0x{stringLiteralsXorKey:X2}. Will rebuild as version {MetadataVersion} metadata with {layout.SectionCount} sections and assemblies section at index {layout.IndexOf(Section.Assemblies)}.");
        Logger.VerboseNewline("Decrypted header: " + string.Join("", decryptedHeader.Select(b => b.ToString("X2"))));

        foreach (var sectionLayout in FindSectionLayouts(originalBytes, headerWords, headerLength, layout))
        {
            Logger.VerboseNewline("Trying section layout: " + string.Join(", ", sectionLayout.Select(range => $"({range.Start:X4}-{range.End:X4})")));

            try
            {
                var rebuiltMetadata = RebuildMetadata(originalBytes, sectionLayout, stringLiteralsXorKey, offsetDelta: layout.OriginalHeaderSize - headerLength, layout);

                Logger.InfoNewline("Returning decrypted metadata now...");
                return rebuiltMetadata;
            }
            catch (Exception e)
            {
                Logger.VerboseNewline($"Section layout rejected: {e.Message}");
            }
        }

        return null;
    }
}
