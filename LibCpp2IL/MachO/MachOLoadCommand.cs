using System;
using System.Text;

namespace LibCpp2IL.MachO;

public class MachOLoadCommand : ReadableClass
{
    public LoadCommandId Command;
    public uint CommandSize;

    public ReadableClass? CommandData;
    public byte[]? UnknownCommandData = null;


    public string? UnknownDataAsString => UnknownCommandData == null ? null : Encoding.UTF8.GetString(UnknownCommandData);

    public override void Read(ClassReadingBinaryReader reader)
    {
        Command = (LoadCommandId)reader.ReadUInt32();
        CommandSize = reader.ReadUInt32();

        switch (Command)
        {
            case LoadCommandId.LC_SEGMENT:
            case LoadCommandId.LC_SEGMENT_64:
            {
                CommandData = reader.ReadReadableHereNoLock<MachOSegmentCommand>();
                break;
            }
            case LoadCommandId.LC_SYMTAB:
            {
                CommandData = reader.ReadReadableHereNoLock<MachOSymtabCommand>();
                break;
            }
            case LoadCommandId.LC_DYLD_INFO:
            case LoadCommandId.LC_DYLD_INFO_ONLY:
            {
                CommandData = reader.ReadReadableHereNoLock<MachODynamicLinkerCommand>();
                break;
            }
            default:
                UnknownCommandData = reader.ReadByteArrayAtRawAddressNoLock(-1, (int)CommandSize - 8); // -8 because we've already read the 8 bytes of the header
                break;
        }
    }
}
