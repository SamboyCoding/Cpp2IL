#if !NET7_0_OR_GREATER
using System.IO;

namespace LibCpp2IL.NintendoSwitch;

internal static class StreamExtensions
{
    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (bytesRead == 0)
                throw new EndOfStreamException("Could not read enough bytes from stream");
            totalRead += bytesRead;
        }
    }
}
#endif
