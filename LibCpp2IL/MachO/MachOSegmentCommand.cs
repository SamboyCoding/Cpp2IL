using System;
using System.Text;

namespace LibCpp2IL.MachO;

public class MachOSegmentCommand : ReadableClass
{
    public string SegmentName = "INVALID"; // 16 bytes

    public ulong VirtualAddress;
    public ulong VirtualSize;
    public ulong FileOffset;
    public ulong FileSize;

    public MachOVmProtection MaxProtection;
    public MachOVmProtection InitialProtection;

    public uint NumSections;

    public MachOSegmentFlags Flags;

    public MachOSection[] Sections = [];

    public override void Read(ClassReadingBinaryReader reader)
    {
        SegmentName = Encoding.UTF8.GetString(reader.ReadByteArrayAtRawAddressNoLock(-1, 16)).TrimEnd('\0');

        VirtualAddress = reader.ReadNUint();
        VirtualSize = reader.ReadNUint();
        FileOffset = reader.ReadNUint();
        FileSize = reader.ReadNUint();

        MaxProtection = (MachOVmProtection)reader.ReadInt32();
        InitialProtection = (MachOVmProtection)reader.ReadInt32();

        NumSections = reader.ReadUInt32();

        Flags = (MachOSegmentFlags)reader.ReadUInt32();

        Sections = new MachOSection[NumSections];
        for (var i = 0; i < NumSections; i++)
        {
            Sections[i] = reader.ReadReadableHereNoLock<MachOSection>();
        }
    }
}
