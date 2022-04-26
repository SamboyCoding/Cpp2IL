using System;
using System.Text;

namespace LibCpp2IL.MachO
{
    public class MachOLoadCommand : ReadableClass
    {
        public LoadCommandId Command;
        public uint CommandSize;

        public ReadableClass? CommandData;
        public byte[]? UnknownCommandData = null;
        
        
        public string? UnknownDataAsString => UnknownCommandData == null ? null : Encoding.UTF8.GetString(UnknownCommandData);
        
        public override void Read(ClassReadingBinaryReader reader)
        {
            Command = (LoadCommandId) reader.ReadUInt32();
            CommandSize = reader.ReadUInt32();

            switch (Command)
            {
                case LoadCommandId.LC_SEGMENT_64:
                    CommandData = reader.ReadReadableHereNoLock<MachOSegmentCommand64>();
                    break;
                default:
                    UnknownCommandData = reader.ReadByteArrayAtRawAddressNoLock(-1, (int) CommandSize - 8); // -8 because we've already read the 8 bytes of the header
                    break;
            }
        }
    }
}