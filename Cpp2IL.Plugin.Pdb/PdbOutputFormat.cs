using System.Reflection.PortableExecutable;
using AssetRipper.Bindings.MsPdbCore;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Plugin.Pdb;

internal unsafe class PdbOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "pdb_windows";

    public override string OutputFormatName => "Windows PDB generation";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        if (!Directory.Exists(outputRoot))
            Directory.CreateDirectory(outputRoot);

        using var peReader = new PEReader(new MemoryStream(context.Binary.GetRawBinaryContent()));

        var pdbFilePath = Path.Combine(outputRoot, "GameAssembly.pdb");
        MsPdbCore.PDBOpen2W(pdbFilePath, "w", out var err, out var openError, out var pdb);

        MsPdbCore.PDBOpenDBI(pdb, "w", "", out var dbi);

        MsPdbCore.DBIOpenModW(dbi, "__Globals", "__Globals", out var mod);

        ushort secNum = 1;
        ushort i2cs = 1;
        foreach (var sectionHeader in peReader.PEHeaders.SectionHeaders)
        {
            if (sectionHeader.Name == "il2cpp")
                i2cs = secNum;
            MsPdbCore.DBIAddSec(dbi, secNum++, 0 /* TODO? */, sectionHeader.VirtualAddress, sectionHeader.VirtualSize);
        }

        Dictionary<string, ulong> keyFunctions = [];

        foreach ((var name, var address) in context.GetOrCreateKeyFunctionAddresses().Pairs)
        {
            keyFunctions[name] = address;
        }

        foreach ((var name, var address) in context.Binary.GetExportedFunctions())
        {
            keyFunctions[name] = address;
        }

        foreach ((var name, var address) in keyFunctions)
        {
            if (address == 0)
                continue;

            GetSectionInformation(peReader, (long)context.Binary.GetRva(address), out var targetSection, out var offset);
            MsPdbCore.ModAddPublic2(mod, name, targetSection, offset, CV_PUBSYMFLAGS_e.Function);
        }

        foreach ((var virtualAddress, var list) in context.MethodsByAddress)
        {
            if (virtualAddress <= 0)
                continue;

            GetSectionInformation(peReader, (long)context.Binary.GetRva(virtualAddress), out var targetSection, out var offset);

            foreach (var method in list)
            {
                if (method is NativeMethodAnalysisContext nativeMethod)
                {
                    continue; // Skip native methods
                }
                MsPdbCore.ModAddPublic2(mod, method.FullName, targetSection, offset, CV_PUBSYMFLAGS_e.Function);
            }
        }

        MsPdbCore.ModClose(mod);
        MsPdbCore.DBIClose(dbi);

        MsPdbCore.PDBCommit(pdb);

        MsPdbCore.PDBQuerySignature2(pdb, out var wrongGuid);

        MsPdbCore.PDBClose(pdb);

        // Hack: manually replace guid and age in generated .pdb, because there's no API on mspdbcore to set them manually
        var targetDebugInfo = peReader.ReadCodeViewDebugDirectoryData(peReader.ReadDebugDirectory()
            .Single(it => it.Type == DebugDirectoryEntryType.CodeView));

        var wrongGuidBytes = wrongGuid.ToByteArray();
        var allPdbBytes = File.ReadAllBytes(pdbFilePath);

        var patchTarget = IndexOfBytes(allPdbBytes, wrongGuidBytes);
        targetDebugInfo.Guid.TryWriteBytes(allPdbBytes.AsSpan(patchTarget));

        Console.WriteLine(targetDebugInfo.Guid);
        Console.WriteLine(targetDebugInfo.Age);

        BitConverter.TryWriteBytes(allPdbBytes.AsSpan(patchTarget - 4), targetDebugInfo.Age);
        File.WriteAllBytes(pdbFilePath, allPdbBytes);
    }

    private static void GetSectionInformation(PEReader peReader, long virtualAddress, out ushort targetSection, out int offset)
    {
        targetSection = 0;
        long tsva = 0;
        ushort sc = 1;
        foreach (var sectionHeader in peReader.PEHeaders.SectionHeaders)
        {
            if (virtualAddress > sectionHeader.VirtualAddress)
            {
                targetSection = sc;
                tsva = sectionHeader.VirtualAddress;
            }
            else
                break;

            sc++;
        }

        if (targetSection == 0)
            throw new ApplicationException("Bad segment");

        offset = (int)(virtualAddress - tsva);
    }

    private static int IndexOfBytes(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
