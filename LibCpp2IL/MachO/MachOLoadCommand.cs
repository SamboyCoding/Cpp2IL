using System;
using System.Text;

namespace LibCpp2IL.MachO
{
    public class MachOLoadCommand
    {
        public LoadCommandId Command;
        public uint CommandSize;

        public object? CommandData;
        public byte[]? UnknownCommandData = null;
        
        
        public string? UnknownDataAsString => UnknownCommandData == null ? null : Encoding.UTF8.GetString(UnknownCommandData);
        
        public void Read(ClassReadingBinaryReader reader)
        {
            Command = (LoadCommandId) reader.ReadUInt32();
            CommandSize = reader.ReadUInt32();

            switch (Command)
            {
                case LoadCommandId.LC_SEGMENT:
                case LoadCommandId.LC_SEGMENT_64:
                    var cmd = new MachOSegmentCommand();
                    cmd.Read(reader);
                    CommandData = cmd;
                    break;
                default:
                    UnknownCommandData = reader.ReadBytes((int) CommandSize - 8); // -8 because we've already read the 8 bytes of the header
                    break;
            }
        }
    }
}