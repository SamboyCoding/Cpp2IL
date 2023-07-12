using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AsmResolver;
using AsmResolver.PE;
using AsmResolver.PE.Win32Resources;
using AssetRipper.Primitives;
using Cpp2IL.Core.Extensions;
using LibCpp2IL;

namespace Cpp2IL.Gui;

public static class GuiUtils
{
    public static UnityVersion ReadFileVersionFromUnityExeXPlatform(string path)
    {
        var pe = PEImage.FromFile(path);

        var versionResourceDirectory = (IResourceDirectory) pe.Resources!.GetEntry(16); //ID 16 is RT_VERSION, from https://docs.microsoft.com/en-us/windows/win32/menurc/resource-types
        var theSingleVersionResource = (IResourceDirectory) versionResourceDirectory.Entries.Single();
        var defaultCultureVersionResource = (IResourceData) theSingleVersionResource.Entries.Single();
        var versionResource = ((DataSegment) defaultCultureVersionResource.Contents!).Data;

        //https://docs.microsoft.com/en-us/windows/win32/menurc/vs-versioninfo
        using var reader = new BinaryReader(new MemoryStream(versionResource));
        var length = reader.ReadUInt16();
        var valueLength = reader.ReadUInt16();
        var isPlainText = reader.ReadUInt16() != 0;
        
        var key = reader.ReadUnicodeString();
        
        if(key != "VS_VERSION_INFO")
            throw new ($"Invalid version resource - invalid key (got {key})");
        
        //Align on 32-bit boundary
        reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;

        //now read fixed file info
        var signature = reader.ReadUInt32();
        if(signature != 0xFEEF04BD)
            throw new ("Invalid version resource - invalid fixed file info signature");

        //Another 48 bytes that I don't actually care about make up the rest of the fixed file info structure
        reader.ReadBytes(0x30);
        
        //Align on 32-bit boundary
        reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
        
        //StringFileInfo
        var sfiBytesRead = 0;
        
        var sfiLen = reader.ReadUInt16();
        var sfiValueLen = reader.ReadUInt16(); //Should be 0
        var sfiIsPlainText = reader.ReadUInt16() != 0;

        sfiBytesRead += 6;

        var sfiKey = reader.ReadUnicodeString();
        if(sfiKey != "StringFileInfo")
            throw new ("Invalid version resource - invalid StringFileInfo key");
        
        sfiBytesRead += (sfiKey.Length + 1) * 2; //+1 for null terminator, *2 for unicode

        //Align on 32-bit boundary
        var oldPos = (int) reader.BaseStream.Position;
        reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
        sfiBytesRead += (int) reader.BaseStream.Position - oldPos;
        
        while (sfiBytesRead < sfiLen)
        {
            //Probably only one table but technically more than one is allowed
            var stringTableBytesRead = 0;
            var stringTableLen = reader.ReadUInt16();
            var stringTableValueLen = reader.ReadUInt16(); //Should be 0
            var stringTableIsPlainText = reader.ReadUInt16() != 0;

            stringTableBytesRead += 6;
            sfiBytesRead += 6;
            
            var stringTableKey = reader.ReadUnicodeString();
            //stringTableKey should be a 8-digit hex number with no base specifier
            if(stringTableKey.Length != 8 || !int.TryParse(stringTableKey, NumberStyles.HexNumber, null, out var stringTableKeyInt))
                throw new ($"Invalid version resource - invalid StringTableKey: {stringTableKey}");

            var langId = stringTableKeyInt >> 16 & 0xFFFF;
            var codePage = stringTableKeyInt & 0xFFFF;
            
            sfiBytesRead += 18; //8 * 2 for the raw chars + 2 for null terminator
            stringTableBytesRead += 18;

            //Align on 32-bit boundary
            oldPos = (int) reader.BaseStream.Position;
            reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
            var delta =(int) reader.BaseStream.Position - oldPos;
            
            sfiBytesRead += delta;
            stringTableBytesRead += delta;

            var stringTable = new Dictionary<string, string>();
            while (stringTableBytesRead < stringTableLen)
            {
                //Individual key-value pairs
                var stringStructLen = reader.ReadUInt16();
                var stringValueLen = reader.ReadUInt16();
                var stringIsPlainText = reader.ReadUInt16() != 0;
                
                stringTableBytesRead += 6;
                sfiBytesRead += 6;
                
                var stringKey = reader.ReadUnicodeString();

                var keyLen = (stringKey.Length + 1) * 2;
                stringTableBytesRead += keyLen;
                sfiBytesRead += keyLen;
                
                //Align on 32-bit boundary
                oldPos = (int) reader.BaseStream.Position;
                reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
                delta = (int) reader.BaseStream.Position - oldPos;
                
                stringTableBytesRead += delta;
                sfiBytesRead += delta;

                var stringValue = reader.ReadUnicodeString();
                var actualValueLen = stringValue.Length + 1; //StringValueLen is in unicode chars, not bytes
                if(actualValueLen != stringValueLen)
                    throw new($"Expecting a string value length of {stringValueLen}, got string {stringValue}, which is {actualValueLen} bytes long");

                stringTable[stringKey] = stringValue;
                
                stringTableBytesRead += actualValueLen * 2;
                sfiBytesRead += actualValueLen * 2;
                
                //Align once more
                oldPos = (int) reader.BaseStream.Position;
                reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
                delta = (int) reader.BaseStream.Position - oldPos;
                
                stringTableBytesRead += delta;
                sfiBytesRead += delta;
            }

            //At least 2019.4 ships a player exe with a "Unity Version" key
            if (stringTable.TryGetValue("Unity Version", out var unityVersion))
            {
                var sanitized = unityVersion[..unityVersion.LastIndexOf('_')]; //strip commit hash
                return UnityVersion.Parse(sanitized);
            }
            
            //Otherwise we can fall back to FileVersion 
            if (stringTable.TryGetValue("FileVersion", out var fileVersion))
            {
                var sanitized = fileVersion[..fileVersion.LastIndexOf('.')]; //strip last bit of build
                return UnityVersion.Parse(sanitized);
            }
        }
        
        return default;
    }
}