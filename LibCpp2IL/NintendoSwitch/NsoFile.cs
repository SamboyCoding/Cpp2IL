using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;

namespace LibCpp2IL.NintendoSwitch
{
    public class NsoFile : Il2CppBinary
    {
        private byte[] _raw;

        private NsoHeader header;
        private bool isTextCompressed;
        private bool isRoDataCompressed;
        private bool isDataCompressed;
        private List<NsoSegmentHeader> segments = new();
        private bool isCompressed => isTextCompressed || isRoDataCompressed || isDataCompressed;


        public NsoFile(MemoryStream input, long maxMetadataUsages) : base(input, maxMetadataUsages)
        {
            _raw = input.GetBuffer();
            is32Bit = false;

            LibLogger.VerboseNewline("\tReading NSO Early Header...");
            header = new()
            {
                Magic = ReadUInt32(),
                Version = ReadUInt32(),
                Reserved = ReadUInt32(),
                Flags = ReadUInt32()
            };

            if (header.Magic != 0x304F534E)
                throw new($"NSO file should have a magic number of 0x304F534E, got 0x{header.Magic:X}");

            LibLogger.VerboseNewline($"\tOK. Magic number is 0x{header.Magic:X}, version is {header.Version}.");

            isTextCompressed = (header.Flags & 1) != 0;
            isRoDataCompressed = (header.Flags & 2) != 0;
            isDataCompressed = (header.Flags & 4) != 0;

            LibLogger.VerboseNewline($"\tCompression flags: text: {isTextCompressed}, rodata: {isRoDataCompressed}, data: {isDataCompressed}.");

            header.TextSegment = new()
            {
                FileOffset = ReadUInt32(),
                MemoryOffset = ReadUInt32(),
                DecompressedSize = ReadUInt32()
            };
            segments.Add(header.TextSegment);

            LibLogger.VerboseNewline($"\tRead text segment header ok. Reading rodata segment header...");

            header.ModuleOffset = ReadUInt32();
            header.RoDataSegment = new()
            {
                FileOffset = ReadUInt32(),
                MemoryOffset = ReadUInt32(),
                DecompressedSize = ReadUInt32()
            };
            segments.Add(header.RoDataSegment);

            LibLogger.VerboseNewline($"\tRead rodata segment header OK. Reading data segment header...");

            header.ModuleFileSize = ReadUInt32();
            header.DataSegment = new()
            {
                FileOffset = ReadUInt32(),
                MemoryOffset = ReadUInt32(),
                DecompressedSize = ReadUInt32()
            };
            segments.Add(header.DataSegment);

            LibLogger.VerboseNewline($"\tRead data segment OK. Reading post-segment fields...");

            header.BssSize = ReadUInt32();
            header.DigestBuildID = ReadBytes(0x20);
            header.TextCompressedSize = ReadUInt32();
            header.RoDataCompressedSize = ReadUInt32();
            header.DataCompressedSize = ReadUInt32();
            header.Padding = ReadBytes(0x1C);

            LibLogger.VerboseNewline("\tRead post-segment fields OK. Reading Dynamic section and Api Info offsets...");

            header.APIInfo = new()
            {
                RegionRoDataOffset = ReadUInt32(),
                RegionSize = ReadUInt32()
            };
            header.DynStr = new()
            {
                RegionRoDataOffset = ReadUInt32(),
                RegionSize = ReadUInt32()
            };
            header.DynSym = new()
            {
                RegionRoDataOffset = ReadUInt32(),
                RegionSize = ReadUInt32()
            };

            LibLogger.VerboseNewline($"\tRead offsets OK. Reading hashes...");

            header.TextHash = ReadBytes(0x20);
            header.RoDataHash = ReadBytes(0x20);
            header.DataHash = ReadBytes(0x20);

            LibLogger.VerboseNewline($"\tRead hashes ok.");

            if (!isCompressed)
            {
                LibLogger.VerboseNewline($"\tBinary is not compressed. Reading BSS segment header...");

                Position = header.TextSegment.FileOffset + 4;
                var modOffset = ReadUInt32();
                Position = header.TextSegment.FileOffset + modOffset + 4;
                var dynamicOffset = ReadUInt32();
                var bssStart = ReadUInt32();
                var bssEnd = ReadUInt32();
                header.BssSegment = new()
                {
                    FileOffset = bssStart,
                    MemoryOffset = bssStart,
                    DecompressedSize = bssEnd - bssStart
                };
                var ehFrameHdrStart = ReadUInt32();
                var ehFrameHdrEnd = ReadUInt32();
            }

            LibLogger.VerboseNewline($"\tNSO Read completed OK.");
        }

        public NsoFile Decompress()
        {
            LibLogger.InfoNewline("\tDecompressing NSO file...");
            if (isTextCompressed || isRoDataCompressed || isDataCompressed)
            {
                var unCompressedStream = new MemoryStream();
                var writer = new BinaryWriter(unCompressedStream);
                writer.Write(header.Magic);
                writer.Write(header.Version);
                writer.Write(header.Reserved);
                writer.Write(0); //Flags
                writer.Write(header.TextSegment.FileOffset);
                writer.Write(header.TextSegment.MemoryOffset);
                writer.Write(header.TextSegment.DecompressedSize);
                writer.Write(header.ModuleOffset);
                var roOffset = header.TextSegment.FileOffset + header.TextSegment.DecompressedSize;
                writer.Write(roOffset); //header.RoDataSegment.FileOffset
                writer.Write(header.RoDataSegment.MemoryOffset);
                writer.Write(header.RoDataSegment.DecompressedSize);
                writer.Write(header.ModuleFileSize);
                writer.Write(roOffset + header.RoDataSegment.DecompressedSize); //header.DataSegment.FileOffset
                writer.Write(header.DataSegment.MemoryOffset);
                writer.Write(header.DataSegment.DecompressedSize);
                writer.Write(header.BssSize);
                writer.Write(header.DigestBuildID);
                writer.Write(header.TextCompressedSize);
                writer.Write(header.RoDataCompressedSize);
                writer.Write(header.DataCompressedSize);
                writer.Write(header.Padding);
                writer.Write(header.APIInfo.RegionRoDataOffset);
                writer.Write(header.APIInfo.RegionSize);
                writer.Write(header.DynStr.RegionRoDataOffset);
                writer.Write(header.DynStr.RegionSize);
                writer.Write(header.DynSym.RegionRoDataOffset);
                writer.Write(header.DynSym.RegionSize);
                writer.Write(header.TextHash);
                writer.Write(header.RoDataHash);
                writer.Write(header.DataHash);
                writer.BaseStream.Position = header.TextSegment.FileOffset;
                Position = header.TextSegment.FileOffset;
                var textBytes = ReadBytes((int)header.TextCompressedSize);
                if (isTextCompressed)
                {
                    var unCompressedData = new byte[header.TextSegment.DecompressedSize];
                    using (var decoder = new Lz4DecodeStream(new MemoryStream(textBytes)))
                    {
                        decoder.Read(unCompressedData, 0, unCompressedData.Length);
                    }

                    writer.Write(unCompressedData);
                }
                else
                {
                    writer.Write(textBytes);
                }

                var roDataBytes = ReadBytes((int)header.RoDataCompressedSize);
                if (isRoDataCompressed)
                {
                    var unCompressedData = new byte[header.RoDataSegment.DecompressedSize];
                    using (var decoder = new Lz4DecodeStream(new MemoryStream(roDataBytes)))
                    {
                        decoder.Read(unCompressedData, 0, unCompressedData.Length);
                    }

                    writer.Write(unCompressedData);
                }
                else
                {
                    writer.Write(roDataBytes);
                }

                var dataBytes = ReadBytes((int)header.DataCompressedSize);
                if (isDataCompressed)
                {
                    var unCompressedData = new byte[header.DataSegment.DecompressedSize];
                    using (var decoder = new Lz4DecodeStream(new MemoryStream(dataBytes)))
                    {
                        decoder.Read(unCompressedData, 0, unCompressedData.Length);
                    }

                    writer.Write(unCompressedData);
                }
                else
                {
                    writer.Write(dataBytes);
                }

                writer.Flush();
                unCompressedStream.Position = 0;
                File.WriteAllBytes("uncompressed.nso",unCompressedStream.ToArray());
                return new NsoFile(unCompressedStream, maxMetadataUsages);
            }

            return this;
        }

        public override long RawLength => _raw.Length;
        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];

        public override long MapVirtualAddressToRaw(ulong addr)
        {
            var segment = segments.First(x => addr >= x.MemoryOffset && addr <= x.MemoryOffset + x.DecompressedSize);
            return (long)(addr - segment.MemoryOffset + segment.FileOffset);
        }

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            var segment = segments.FirstOrDefault(x => offset >= x.FileOffset && offset <= x.FileOffset + x.DecompressedSize);
            if (segment == null)
            {
                return 0;
            }
            return offset - segment.FileOffset + segment.MemoryOffset;
        }

        public override ulong GetRVA(ulong pointer)
        {
            return pointer;
        }

        public override byte[] GetRawBinaryContent() => _raw;

        public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
        {
            return 0;
        }

        public override byte[] GetEntirePrimaryExecutableSection()
        {
            return _raw.Skip((int)header.TextSegment.FileOffset).Take((int)header.TextSegment.DecompressedSize).ToArray();
        }

        public override ulong GetVirtualAddressOfPrimaryExecutableSection()
        {
            return header.TextSegment.MemoryOffset;
        }
    }
}