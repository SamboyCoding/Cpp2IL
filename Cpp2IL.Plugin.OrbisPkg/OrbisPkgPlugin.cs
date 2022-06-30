using System.Buffers;
using System.IO.MemoryMappedFiles;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Plugin.OrbisPkg;
using LibOrbisPkg.PFS;
using LibOrbisPkg.PKG;
using LibOrbisPkg.Util;

[assembly:RegisterCpp2IlPlugin(typeof(OrbisPkgPlugin))]

namespace Cpp2IL.Plugin.OrbisPkg;

public class OrbisPkgPlugin : Cpp2IlPlugin
{
    public override string Name => "Orbis PKG";
    public override string Description => "Provides support for loading Orbis PKG files and extracting the binary and metadata files.";

    public override void OnLoad()
    {
        //No-op
    }

    public override bool HandleGamePath(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        Logger.Verbose($"Checking file {gamePath} to see if it's an Orbis PKG...");

        if (!File.Exists(gamePath))
        {
            Logger.VerboseNewline("File doesn't exist.");
            return false;
        }

        //Check header
        using var mmf = MemoryMappedFile.CreateFromFile(gamePath);

        var header = new byte[4];
        using (var headerStream = mmf.CreateViewStream(0, 4, MemoryMappedFileAccess.Read))
        {
            if (headerStream.Read(header, 0, 4) != 4)
            {
                Logger.VerboseNewline("Failed to read 4-byte header.");
                return false;
            }
        }

        var isOrbis = header[0] == 0x7F;
        isOrbis |= header[1] == 0x43;
        isOrbis |= header[2] == 0x4E;
        isOrbis |= header[3] == 0x54;

        if (!isOrbis)
        {
            Logger.VerboseNewline("Header doesn't match, not an Orbis PKG.");
            return false;
        }

        Logger.VerboseNewline("Probable orbis PKG detected. Attempting to load.");

        Pkg pkg;
        try
        {
            using var pkgStream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            var reader = new PkgReader(pkgStream);

            pkg = reader.ReadPkg();
        }
        catch (Exception e)
        {
            Logger.WarnNewline($"Exception reading PKG: {e}");
            return false;
        }

        Logger.VerboseNewline("PKG loaded successfully. Checking PFS header...");

        //What we want is in the PFS.
        PfsHeader pfsHeader;

        try
        {
            using var pfsStream = mmf.CreateViewStream((long)pkg.Header.pfs_image_offset, (long)pkg.Header.pfs_image_size, MemoryMappedFileAccess.Read);
            pfsHeader = PfsHeader.ReadFromStream(pfsStream);
        }
        catch (Exception e)
        {
            Logger.WarnNewline($"Exception reading PFS: {e}");
            return false;
        }

        Logger.VerboseNewline("PFS header loaded. Checking encryption state...");

        byte[] ekpfs;
        if (pfsHeader.Mode.HasFlag(PfsMode.Encrypted))
        {
            Logger.Verbose("PFS is encrypted. Please enter the 32-character passcode: ");
            var passcode = Console.ReadLine()?.Trim();

            if (pkg.CheckPasscode(passcode))
            {
                ekpfs = Crypto.ComputeKeys(pkg.Header.content_id, passcode, 1);
                Logger.VerboseNewline("Passcode accepted. Decrypting PFS...");
            }
            else
            {
                Logger.WarnNewline("Passcode is incorrect. Aborting.");
                return false;
            }
        }
        else
        {
            Logger.VerboseNewline("PFS is not encrypted. Reading full PFS...");
            ekpfs = pkg.GetEkpfs();
        }

        using var va = mmf.CreateViewAccessor((long)pkg.Header.pfs_image_offset, (long)pkg.Header.pfs_image_size, MemoryMappedFileAccess.Read);
        var outerPfs = new PfsReader(va, pkg.Header.pfs_flags, ekpfs);
        var inner = new PfsReader(new PFSCReader(outerPfs.GetFile("pfs_image.dat").GetView()));

        Logger.VerboseNewline("PFS decrypted. Checking for binary and metadata files...");

        var foundMeta = false;
        var foundBin = false;
        var foundVersion = false;

        foreach (var file in inner.GetAllFiles())
        {
            if (file.name == "global-metadata.dat")
            {
                Logger.Verbose("Found metadata file. Extracting to temporary file path...");
                args.PathToMetadata = ExtractToTemporaryPath(file);
                foundMeta = true;
            }
            else if (file.name == "Il2CppUserAssemblies.prx")
            {
                Logger.Verbose("Found binary file. Extracting to temporary file path...");
                args.PathToAssembly = ExtractToTemporaryPath(file);
                foundBin = true;
            }
            else if (file.name == "globalgamemanagers")
            {
                Logger.Verbose("Found ggm file - extracting unity version...");
                
                //Read first ~0x40 bytes
                var ggmStream = file.GetView();
                var count = (int) Math.Min(0x40, file.size);
                var ggmBytes = ArrayPool<byte>.Shared.Rent(count);
                
                ggmStream.Read(0, ggmBytes, 0, count);
                
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
                Logger.VerboseNewline($"Got version {args.UnityVersion}");
                foundVersion = true;
                
                ArrayPool<byte>.Shared.Return(ggmBytes);
            }
            else if (file.name == "data.unity3d")
            {
                Logger.Verbose("Found data.unity3d file - extracting unity version...");
                var tempFile = Path.GetTempFileName();
                file.Save(tempFile, true);
                
                using (var s = File.OpenRead(tempFile))
                    args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(s);
                
                Logger.VerboseNewline($"Got version {args.UnityVersion}");
                foundVersion = true;
                File.Delete(tempFile);
            }
        }
        
        args.Valid = foundMeta && foundBin && foundVersion;

        return args.Valid;
    }

    private string ExtractToTemporaryPath(PfsReader.File file)
    {
        var path = GetTemporaryFilePath();
        file.Save(path, true);
        Logger.VerboseNewline($"Extracted to {path}");
        return path;
    }
}