using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AssetRipper.Primitives;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;

[assembly:RegisterCpp2IlPlugin(typeof(Cpp2IL.Plugin.Mfuscator.MfuscatorSupportPlugin))]

namespace Cpp2IL.Plugin.Mfuscator;

public class MfuscatorSupportPlugin : Cpp2IlPlugin
{
    private const int MaxHeaderSize = 480; //somewhat arbitrary

    private const int StringLiteralsSectionIndex = 0;
    private const int StringLiteralsDataSectionIndex = 1;
    private const int StringsSectionIndex = 2;
    private const int PropertiesSectionIndex = 4;
    private const int MethodsSectionIndex = 5;
    private const int FieldsSectionIndex = 11;

    private record struct ReconstructedSection(int OffsetAccordingToHeader, int Length, int Delta)
    {
        public int ActualOffset => OffsetAccordingToHeader + Delta;
    }

    private record struct DeadEnd(int depth, int deadEndNumber, int actualPos, string reason, List<ReconstructedSection> sections) : IComparable<DeadEnd>
    {
        public int CompareTo(DeadEnd other)
        {
            //inverted so largest first
            return other.depth.CompareTo(depth);
        }
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
    
    private static void CyclicXorHeader(ReadOnlySpan<byte> data, Span<byte> output, byte xorKey, bool isPlus, int offset = 0)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var keyByte = (byte) ((isPlus 
                ? (xorKey + offset + i) 
                : (xorKey - (offset + i))) & 0xFF);
            output[i] = (byte) (data[i] ^ keyByte);
        }
    }

    private static void CyclicXor(ReadOnlySpan<byte> data, Span<byte> output, byte xorKey, bool isPlus, int offset = 0)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var keyByte = (byte) ((isPlus 
                ? (xorKey + offset + i) 
                : (i - offset - xorKey)) & 0xFF);
            output[i] = (byte) (data[i] ^ keyByte);
        }
    }
    
    private static byte DeriveXorKey(Span<byte> encryptedHeader, out bool isPlus)
    {
        var valueOffset = 0;
        while (valueOffset + 4 + 3 < encryptedHeader.Length)
        {
            var knownZeroByte = encryptedHeader[valueOffset + 3];
            var knownZeroByteTwo = encryptedHeader[valueOffset + 4 + 3];
            
            var looksLikePlus = ((int) knownZeroByte + 4) % 256 == knownZeroByteTwo;
            var looksLikeMinus = ((int) knownZeroByte - 4) % 256 == knownZeroByteTwo;

            if (!looksLikeMinus && !looksLikePlus)
            {
                valueOffset += 4;
                continue;
            }
            
            isPlus = looksLikePlus;
            return (byte) ((isPlus 
                ? (knownZeroByte - valueOffset - 3)
                : (knownZeroByte + valueOffset + 3)
            ) & 0xFF);
        }

        throw new Exception("Failed to derive XOR key");
    }

    private byte[] DecryptHeader(Span<byte> encryptedHeader, out byte stringLiteralsXorKey, out bool stringLiteralsIsPlus)
    {
        var xorKey = DeriveXorKey(encryptedHeader, out var isPlus);
        
        Logger.VerboseNewline($"Derived header XOR key: 0x{xorKey:X2}. Header fields use {(isPlus ? "plus" : "minus")} rotation.");

        //Header size isn't actually known, we just pass the first 480 bytes in
        //So let's work it out
        var headerSize = 0;
        
        Span<byte> decryptedWord = stackalloc byte[4];
        while (headerSize < MaxHeaderSize)
        {
            var encryptedWord = encryptedHeader[headerSize..(headerSize + 4)];
            CyclicXorHeader(encryptedWord, decryptedWord, xorKey, isPlus, headerSize);

            headerSize += 4;

            //Top byte of every header field is expected to be 0 (ie no metadata offset or length is > 32mb), so when we find a non-zero byte we've reached end of header and grabbed the beginning of the string literal data
            if (decryptedWord[0] == 0)
            {
                //Still in the header
                continue;
            }

            //We've reached the string literal data, so we can stop now. We just need to determine whether the string literals use plus or minus key rotation to know how to decode them later.
            var stringLiteralsLookLikePlus = ((encryptedWord[0] + 1) & 0xFF) == encryptedWord[1];
            var stringLiteralsLookLikeMinus = ((encryptedWord[0] - 1) & 0xFF) == encryptedWord[1];
            
            if(!stringLiteralsLookLikeMinus && !stringLiteralsLookLikePlus)
                continue; //not this one.
            
            //The first 4 bytes of the string literals section should all be 0, i.e. they should be sequential bytes when a sequential XOR is applied. Check this.
            var addend = stringLiteralsLookLikePlus ? 1 : (stringLiteralsLookLikeMinus ? -1 : 0);
            
            var looksValid = true;
            for (var i = 0; i < 3; i++)
            {
                if(((encryptedWord[i] + addend) & 0xFF) != encryptedWord[i + 1])
                {
                    looksValid = false;
                    break;
                }
            }

            if (looksValid)
            {
                headerSize -= 4; //the last 4 bytes we read were actually the start of the string literal data, so remove them from the header size
                
                var decryptedHeader = new byte[headerSize];
                CyclicXorHeader(encryptedHeader[..headerSize], decryptedHeader, xorKey, isPlus);
                stringLiteralsXorKey = encryptedWord[0];
                var nextEncryptedWord = encryptedHeader[(headerSize + 4)..(headerSize + 8)];
                stringLiteralsIsPlus = nextEncryptedWord[0] == encryptedWord[0] + 4;
                return decryptedHeader;
            }
        }
        
        throw new Exception("Failed to determine header size");
    }

    private List<List<ReconstructedSection>> FindPathsThroughMetadata(uint[] headerWords, int dataStart, int fileEnd, out SortedCollection<DeadEnd> bestDeadEnds, int maxResults = 10, int debugBestN = 10, int? expectedSectionCount = null, Dictionary<int, int>? alignBefore = null, int? originalHeaderSize = null)
    {
        alignBefore ??= new();
        var realOriginalHeaderSize = originalHeaderSize ?? dataStart;
        var maxAlignPad = alignBefore.Values.DefaultIfEmpty(1).Max() - 1;

        var totalDeadEnds = 0;
        var deadEndCounter = 0;
        List<DeadEnd> deadEnds = new();
        var localBestDeadEnds = bestDeadEnds = new();
        
        //Keep track of how many times each word appears so we can find a path using only the values which exist
        var pool = new SortedCollection<uint>(headerWords);

        List<List<ReconstructedSection>> results = new();

        DepthFirstSearch(dataStart, pool, []);
        
        return results;

        void TrackDeadEnd(int actualPos, List<ReconstructedSection> sections, string reason)
        {
            if(debugBestN <= 0)
                return; //we're not tracking dead ends, so ignore this
            
            totalDeadEnds++;
            deadEndCounter++;
            var depth = sections.Count;
            
            if(localBestDeadEnds.Count > 0 && depth < localBestDeadEnds[0].depth)
                return; //we've already got better dead ends, so ignore this one

            var entry = new DeadEnd(depth, deadEndCounter, actualPos, reason, sections.ToList());
            deadEnds.Add(entry);
            
            localBestDeadEnds.Add(entry);
            if (localBestDeadEnds.Count > debugBestN)
                localBestDeadEnds.RemoveAt(localBestDeadEnds.Count - 1);
        }

        bool OffsetInRange(uint candidateOffset, int actualPos)
        {
            const int MinDelta = 0x10;
            const int MaxDelta = 0x40;
            
            var delta = Math.Abs(actualPos - candidateOffset);
            return delta is >= MinDelta and <= MaxDelta;
        }
        
        //Alignment is according to the header before it was mangled, i.e. with original header size
        int ApplyAlignment(int actualPos, int sectionIndex)
        {
            if(!alignBefore.TryGetValue(sectionIndex, out var align))
                return actualPos;

            var originalOffset = realOriginalHeaderSize + (actualPos - dataStart);
            var remainder = originalOffset % align;
            if (remainder == 0)
                return actualPos;
            
            var padding = align - remainder;
            return actualPos + padding;
        }

        void DepthFirstSearch(int actualPos, SortedCollection<uint> remainingPool, List<ReconstructedSection> sections)
        {
            if(results.Count >= maxResults)
                return;
            
            var sectionIndex = sections.Count;
            actualPos = ApplyAlignment(actualPos, sectionIndex);
            
            var shortfall = fileEnd - actualPos;
            if (shortfall <= 0)
            {
                if(expectedSectionCount == null || sections.Count == expectedSectionCount)
                    results.Add([..sections]);
                
                return; //we've gone past the end of the file, so this is invalid
            }

            var candidateOffsets = new List<uint>();
            foreach (var offset in remainingPool)
            {
                if(OffsetInRange(offset, actualPos))
                    candidateOffsets.Add(offset);
            }

            if (candidateOffsets.Count == 0)
            {
                TrackDeadEnd(actualPos, sections, "No valid candidate offsets");
                return; //no more valid offsets, so this is a dead end
            }

            var anyLengthFound = false;
            candidateOffsets.Sort();
            for (var i = 0; i < candidateOffsets.Count; i++)
            {
                var candidateOffset = candidateOffsets[i];
                remainingPool.Remove(candidateOffset);
                var delta = actualPos - candidateOffset;

                var foundLength = false;
                for (var j = 0; j < remainingPool.Count; j++)
                {
                    var length = remainingPool[j];
                    var newPos = (int)(actualPos + length);
                    if (newPos > fileEnd + maxAlignPad)
                        break; //lengths are sorted ascending, so if this length is too long then the rest will be too

                    //this cuts down on the number of invalid paths we get quite significantly
                    if (sectionIndex < 26 && length == 0)
                        //don't allow zero lengths for the first 26 sections
                        continue;

                    remainingPool.Remove(length);

                    foundLength = true;
                    sections.Add(new ReconstructedSection((int)candidateOffset, (int)length, (int)delta));

                    DepthFirstSearch(newPos, remainingPool, sections);

                    sections.RemoveAt(sections.Count - 1);
                    remainingPool.Add(length);
                }

                if (foundLength)
                    anyLengthFound = true;

                remainingPool.Add(candidateOffset);
            }

            if (!anyLengthFound)
            {
                TrackDeadEnd(actualPos, sections, "No valid length found for any candidate offset");
            }
        }
    }
    
    private Dictionary<int, byte[]> DecryptEncryptedSections(byte[] encryptedMetadata, List<(int Start, int End)> sections, byte stringLiteralsXorKey, bool stringLiteralsIsPlus, int assembliesSectionIndex)
    {
        var decryptedSectionBytes = new Dictionary<int, byte[]>();
        
        //Use the size of the section and the information we worked out earlier to derive the key shared between all sections
        var stringLiteralsStart = sections[StringLiteralsSectionIndex].Start;
        var stringLiteralsSize = sections[StringLiteralsSectionIndex].End - sections[StringLiteralsSectionIndex].Start;

        foreach (var usingOffsetNotSize in stackalloc bool[] { true, false })
        {
            try
            {
                byte sectionsXorKeyAddend = 0;
                var stringLiteralsKeyComponent = usingOffsetNotSize ? stringLiteralsStart : stringLiteralsSize;
                var testAddend = (byte)((stringLiteralsIsPlus ? (stringLiteralsXorKey - stringLiteralsKeyComponent) : (stringLiteralsXorKey + stringLiteralsKeyComponent)) & 0xFF);

                //Now decrypt the string literals section
                var decryptedLiterals = new byte[stringLiteralsSize];
                CyclicXor(
                    encryptedMetadata.AsSpan(sections[StringLiteralsSectionIndex].Start, stringLiteralsSize),
                    decryptedLiterals,
                    testAddend,
                    stringLiteralsIsPlus,
                    stringLiteralsKeyComponent
                );

                if (decryptedLiterals[0] == 0 && decryptedLiterals[1] == 0)
                {
                    sectionsXorKeyAddend = testAddend;
                    decryptedSectionBytes[StringLiteralsSectionIndex] = decryptedLiterals;
                }
                
                if (!decryptedSectionBytes.ContainsKey(StringLiteralsSectionIndex))
                    throw new Exception("Failed to determine whether section keys are based on offsets or sizes");
                
                Logger.VerboseNewlineIfDebug($"Section keys are based on {(usingOffsetNotSize ? "offsets" : "sizes")}, with addend 0x{sectionsXorKeyAddend:X2}");

                //String literal data starts with 2 00 bytes, so we can get the direction from that
                var stringLiteralDataStart = sections[StringLiteralsDataSectionIndex].Start;
                var stringLiteralDataSize = sections[StringLiteralsDataSectionIndex].End - sections[StringLiteralsDataSectionIndex].Start;
                var stringLiteralDataKeyComponent = usingOffsetNotSize ? stringLiteralDataStart : stringLiteralDataSize;

                var firstByte = encryptedMetadata[stringLiteralDataStart];
                var secondByte = encryptedMetadata[stringLiteralDataStart + 1];
                var stringLiteralDataIsPlus = ((firstByte + 1) & 0xFF) == secondByte;
                var stringLiteralDataIsMinus = ((firstByte - 1) & 0xFF) == secondByte;
                if (!stringLiteralDataIsPlus && !stringLiteralDataIsMinus)
                    throw new Exception("Failed to determine string literal data XOR direction");

                if (stringLiteralDataIsPlus)
                {
                    //check for underflow resulting in wrong initial key
                    var encryptedFirstWord = encryptedMetadata.AsSpan(stringLiteralDataStart, 4);
                    var decryptedFirstWord = new byte[4];
                    CyclicXor(
                        encryptedFirstWord,
                        decryptedFirstWord,
                        sectionsXorKeyAddend,
                        true,
                        stringLiteralDataKeyComponent
                    );
                    if (decryptedFirstWord[0] != 0)
                    {
                        stringLiteralDataIsPlus = false;
                        sectionsXorKeyAddend = (byte)((0 - stringLiteralsXorKey - stringLiteralsKeyComponent) & 0xFF);
                    }
                }

                //And decrypt it
                var decryptedLiteralData = decryptedSectionBytes[StringLiteralsDataSectionIndex] = new byte[stringLiteralDataSize];
                CyclicXor(
                    encryptedMetadata.AsSpan(stringLiteralDataStart, stringLiteralDataSize),
                    decryptedLiteralData,
                    sectionsXorKeyAddend,
                    stringLiteralDataIsPlus,
                    stringLiteralDataKeyComponent
                );

                //Strings are a bit harder, we need to look for the null terminators in the first 32 bytes
                var stringsSectionStart = sections[StringsSectionIndex].Start;
                var stringsSectionSize = sections[StringsSectionIndex].End - sections[StringsSectionIndex].Start;
                var stringsSectionKeyComponent = usingOffsetNotSize ? stringsSectionStart : stringsSectionSize;
                var stringsFirstXorByteOffset = 0;
                var stringsIsPlus = false;
                var foundZeroBytes = 0;
                foreach (var testIsPlus in new bool[] { true, false })
                {
                    foundZeroBytes = 0;
                    stringsIsPlus = testIsPlus;
                    for (var i = 0; i < 32; i++)
                    {
                        var assumedXorKey = (byte)((testIsPlus
                            ? (i + stringsSectionKeyComponent + sectionsXorKeyAddend)
                            : (i - stringsSectionKeyComponent - sectionsXorKeyAddend)) & 0xFF);
                        var xorByte = (byte)(encryptedMetadata[stringsSectionStart + i] ^ assumedXorKey);
                        if (xorByte == 0)
                        {
                            foundZeroBytes++;
                            if (foundZeroBytes == 1)
                                stringsFirstXorByteOffset = i;
                            else if (foundZeroBytes == 2)
                                break; //we've found the first two null terminators, which is enough to be confident we've got the right key direction
                        }
                    }

                    if (foundZeroBytes == 2)
                        break;
                }

                if (foundZeroBytes != 2)
                    throw new Exception("Failed to determine strings section XOR direction");

                //sanity check
                var stringsXorByte = (byte)((stringsIsPlus
                    ? (stringsFirstXorByteOffset + stringsSectionKeyComponent + sectionsXorKeyAddend)
                    : (stringsFirstXorByteOffset - stringsSectionKeyComponent - sectionsXorKeyAddend)) & 0xFF);

                if (encryptedMetadata[stringsSectionStart + stringsFirstXorByteOffset] != stringsXorByte)
                    throw new Exception("Strings section XOR key doesn't seem to be correct");

                //ok now decrypt strings
                var decryptedStrings = decryptedSectionBytes[StringsSectionIndex] = new byte[stringsSectionSize];
                CyclicXor(
                    encryptedMetadata.AsSpan(stringsSectionStart, stringsSectionSize),
                    decryptedStrings,
                    sectionsXorKeyAddend,
                    stringsIsPlus,
                    stringsSectionKeyComponent
                );

                //for the rest of the sections we can just check the 3rd byte is 0 to determine the direction
                var remainingEncryptedSections = new int[] { PropertiesSectionIndex, MethodsSectionIndex, FieldsSectionIndex, assembliesSectionIndex };
                foreach (var sectionIndex in remainingEncryptedSections)
                {
                    var sectionStart = sections[sectionIndex].Start;
                    var sectionSize = sections[sectionIndex].End - sections[sectionIndex].Start;
                    var sectionKeyComponent = usingOffsetNotSize ? sectionStart : sectionSize;

                    var decryptedSection = new byte[sectionSize];
                    foreach (var testIsPlus in new bool[] { true, false })
                    {
                        CyclicXor(
                            encryptedMetadata.AsSpan(sectionStart, sectionSize),
                            decryptedSection,
                            sectionsXorKeyAddend,
                            testIsPlus,
                            sectionKeyComponent
                        );
                        if (decryptedSection[3] == 0)
                        {
                            decryptedSectionBytes[sectionIndex] = decryptedSection;
                            break;
                        }
                    }

                    if (!decryptedSectionBytes.ContainsKey(sectionIndex))
                        throw new Exception($"Failed to determine XOR direction for section at index {sectionIndex}");
                }

                return decryptedSectionBytes;
            }
            catch (Exception)
            {
                continue;
            }
        }
        
        throw new Exception("Failed to decrypt sections with either offset-based or size-based keys");
    }
    
    private byte[] RebuildMetadata(byte[] encryptedMetadata, List<(int Start, int End)> sections, byte stringLiteralsXorKey, bool stringLiteralsIsPlus, int offsetDelta, byte metadataVersion, int assembliesSectionIndex)
    {
        var decryptedSections = DecryptEncryptedSections(encryptedMetadata, sections, stringLiteralsXorKey, stringLiteralsIsPlus, assembliesSectionIndex);
        
        var decryptedMetadata = new byte[encryptedMetadata.Length];
        Span<byte> magicAndVersion = [0xAF, 0x1B, 0xB1, 0xFA, metadataVersion, 0x00, 0x00, 0x00];
        magicAndVersion.CopyTo(decryptedMetadata);
        
        var headerSpan = decryptedMetadata.AsSpan(8, 256 - 8);

        for (var i = 0; i < sections.Count; i++)
        {
            var (start, end) = sections[i];
            
            //Write offset and length to header
            var offsetBytes = BitConverter.GetBytes(start + offsetDelta).AsSpan();
            var lengthBytes = BitConverter.GetBytes(end - start).AsSpan();
            offsetBytes.CopyTo(headerSpan);
            lengthBytes.CopyTo(headerSpan[4..]);
            headerSpan = headerSpan[8..];

            //And copy over data
            var sectionSpan = decryptedMetadata.AsSpan(start + offsetDelta, end - start);
            //Decrypted if it was encrypted, else copy straight from the original file
            var sectionData = decryptedSections.GetValueOrDefault(i) ?? encryptedMetadata.AsSpan(start, end - start).ToArray();
            sectionData.CopyTo(sectionSpan);
        }

        return decryptedMetadata;
    }

    private byte[]? TryFixupMfuscatorMetadata(byte[] originalBytes, UnityVersion unityVersion)
    {
        var decryptedHeader = DecryptHeader(originalBytes, out var stringLiteralsXorKey, out var stringLiteralsIsPlus);
        
        var headerLength = decryptedHeader.Length;
         
        var headerWords = MemoryMarshal.Cast<byte, uint>(decryptedHeader).ToArray();
        
        //There is some garbage data at the end of the file, which confuses the actual length of the metadata (which we use to find a chain through the real/fake values in the header to identify the real ones)
        //So we unfortunately have to bruteforce it, reducing the length of the metadata by 4 bytes at a time until we get a path.
        var metadataLength = originalBytes.Length;

        var sectionAlignments = new Dictionary<int, int>
        {
            { 8, 8 }, //fieldAndParameterDefaultValueData
        };

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

        var assembliesSectionIndex = 21;
        if (MetadataVersion > 103)
            assembliesSectionIndex = 22; //typeInlineArrays added before it
        else if (MetadataVersion == 24 && unityVersion.LessThan(2019))
            assembliesSectionIndex = 22; //pre-24.2 we have rgctxEntries before assemblies
        
        var expectedSectionCount = MetadataVersion switch
        {
            >= 104 => 32,
            >= 27 => 31,
            _ => throw new NotImplementedException("Metadata versions below 27 aren't currently supported (largely because mfuscator itself doesn't support these versions)")
        };
        var bytesPerSectionHeaderField = MetadataVersion switch
        {
            >= 38 => 12,
            _ => 8
        };
        
        if(bytesPerSectionHeaderField == 12)
            throw new NotImplementedException("Metadata versions with 12 bytes per section header field aren't currently supported");
        
        var originalHeaderSize = 8 + expectedSectionCount * bytesPerSectionHeaderField; //magic + version + 8 bytes per section header field
        
        Logger.InfoNewline($"Mfuscator header decrypted successfully. Header length: {headerLength} bytes. String literals XOR key: 0x{stringLiteralsXorKey:X2}. String literals use {(stringLiteralsIsPlus ? "plus" : "minus")} rotation. Will rebuild as version {MetadataVersion} metadata with assemblies section at index {assembliesSectionIndex}.");
        
        Logger.VerboseNewline("Decrypted header: " + string.Join("", decryptedHeader.Select(b => b.ToString("X2"))));
        
        var lengthsToTry = Enumerable.Sequence(metadataLength, headerLength, -4).ToArray();
        byte[]? rebuiltMetadata = null;
        var winningIndex = long.MaxValue;
        var rebuiltMetadataLock = new object();

        // Preserve the original highest-length-first behavior while still stopping lower-priority work once a candidate is found.
        Parallel.ForEach(Partitioner.Create(lengthsToTry, loadBalance: true), (length, loopState, index) =>
        {
            if (index > loopState.LowestBreakIteration || index > Interlocked.Read(ref winningIndex))
                return;

            Logger.VerboseNewlineIfDebug($"Trying metadata length 0x{length:X4}");
            
            var paths = FindPathsThroughMetadata(headerWords, headerLength, length, out var bestDeadEnds, maxResults: 65536, debugBestN: 0, expectedSectionCount: expectedSectionCount, alignBefore: sectionAlignments, originalHeaderSize: originalHeaderSize);

            if (paths.Count > 0)
            {
                //We'll likely get a couple dozen paths due to the fake offsets, which vary in supposed position and delta, but they should all agree on *actual* position in file.
                //We check that that's the case, and take those actual positions as gospel.
                //NB actually we don't check if that's the case because they sometimes differ in unimportant sections, too bad!
                Logger.VerboseNewlineIfDebug($"Found {paths.Count} possible section layouts with metadata length 0x{length:X4} bytes.");
                
                var actualRanges = paths.Select(path => path.Select(section => (section.ActualOffset, section.ActualOffset + section.Length)).ToArray()).ToArray();

                var distinct = actualRanges.Distinct(new SectionRangeComparer()).ToArray();
                
                Logger.VerboseNewlineIfDebug($"These collapse to {distinct.Length} distinct actual section layouts.");

                foreach (var acceptedLayout in distinct)
                {

                    Logger.VerboseNewlineIfDebug($"Trying section layout: " + string.Join(", ", acceptedLayout.Select(range => $"({range.Item1:X4}-{range.Item2:X4})")));

                    try
                    {
                        var ret = RebuildMetadata(originalBytes, acceptedLayout.ToList(), stringLiteralsXorKey, stringLiteralsIsPlus, offsetDelta: originalHeaderSize - headerLength, MetadataVersion, assembliesSectionIndex);
                        var installedWinningResult = false;
                        lock (rebuiltMetadataLock)
                        {
                            if (index < winningIndex)
                            {
                                winningIndex = index;
                                rebuiltMetadata = ret;
                                installedWinningResult = true;
                            }
                        }

                        if (!installedWinningResult)
                            return;

                        Logger.InfoNewline("Returning decrypted metadata now...");
                        loopState.Break();
                        return;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }

        });
        
        return rebuiltMetadata;
    }
}
