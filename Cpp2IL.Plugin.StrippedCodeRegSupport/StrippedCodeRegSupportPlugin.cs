using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Plugin.StrippedCodeRegSupport;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

[assembly: RegisterCpp2IlPlugin(typeof(StrippedCodeRegSupportPlugin))]

namespace Cpp2IL.Plugin.StrippedCodeRegSupport;

public class StrippedCodeRegSupportPlugin : Cpp2IlPlugin
{
    private const float MinSupportedMetadataVersion = 27f;

    public override string Name => "Stripped CodeReg Support";
    public override string Description => "Aims to help support games with CodeRegistration structs that have been inlined away by the compiler.";

    public override void OnLoad()
    {
        RegisterBinaryRegistrationFuncFallbackHandler(OnReadFail);
    }

    private void OnReadFail(Il2CppBinary binary, Il2CppMetadata metadata, ref Il2CppCodeRegistration? codereg, ref Il2CppMetadataRegistration? metareg)
    {
        if (codereg != null)
            //We already have a CodeRegistration, so we don't need to do anything.
            return;

        //We don't have a CodeRegistration, so we need to try and find one.
        Logger.InfoNewline("Received read failure for CodeRegistration, implying it may have been stripped. Attempting to work around...");

        if (LibCpp2IlMain.MetadataVersion < MinSupportedMetadataVersion)
        {
            Logger.ErrorNewline($"This game's metadata version is too old to support this plugin (it needs to be at least Metadata {MinSupportedMetadataVersion:F1}, i.e. Unity 2020.2 or newer).");
            return;
        }

        //All we NEED to find is pCodegenModules - the rest of the CodeRegistration struct isn't critical to a successful dump.
        //We can piggyback off BinarySearcher:
        var searcher = new BinarySearcher(binary, metadata.methodDefs.Length, metadata.typeDefs.Length);

        var mscorlibs = searcher.FindAllStrings("mscorlib.dll\0").Select(binary.MapRawAddressToVirtual).ToList();

        Logger.VerboseNewline($"Found {mscorlibs.Count} occurrences of mscorlib.dll: [{string.Join(", ", mscorlibs.Select(p => p.ToString("X")))}]");

        var pMscorlibCodegenModule = searcher.FindAllMappedWords(mscorlibs).ToList(); //CodeGenModule address will be in here

        Logger.VerboseNewline($"Found {pMscorlibCodegenModule.Count} potential codegen modules for mscorlib: [{string.Join(", ", pMscorlibCodegenModule.Select(p => p.ToString("X")))}]");

        //This gives us the address of the CodeGenModule struct for mscorlib.dll, which is *somewhere* in the CodeGenModules list - near the end, but rarely AT the end.
        var pMscorlibCodegenEntryInCodegenModulesList = searcher.FindAllMappedWords(pMscorlibCodegenModule).ToList(); //CodeGenModules list address will be in here

        var moduleCount = (ulong)metadata.AssemblyDefinitions.Length;

        for (var i = 0; i < pMscorlibCodegenEntryInCodegenModulesList.Count; i++)
        {
            var pSomewhereInCodegenModules = pMscorlibCodegenEntryInCodegenModulesList[i];

            var startOfCodegenModulesList = FindCodeGenModulesListFromMscorlib(binary, pSomewhereInCodegenModules, moduleCount, out var endOfCodegenModulesList);

            //Try to read the first entry as a codegen module pointer.
            var firstModulePtr = binary.ReadPointerAtVirtualAddress(startOfCodegenModulesList);
            if (firstModulePtr == 0)
            {
                Logger.VerboseNewline($"Found start of CodeGenModules list at 0x{startOfCodegenModulesList:X}, but first entry is null. Continuing search...");
                continue;
            }

            //Now try to read that as a CGM
            Il2CppCodeGenModule? firstModule = null;
            try
            {
                firstModule = binary.ReadReadableAtVirtualAddress<Il2CppCodeGenModule>(firstModulePtr);
                if (firstModule?.Name is not { } name || string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAscii(c)))
                {
                    Logger.VerboseNewline($"Discarding CodeGenModules list at 0x{startOfCodegenModulesList:X} because first module name is invalid.");
                    continue;
                }
            }
            catch (Exception e)
            {
                Logger.VerboseNewline($"Discarding CodeGenModules list at 0x{startOfCodegenModulesList:X} because we hit a {e.GetType().Name} when trying to read it as a CGM array.");
                continue;
            }

            Logger.VerboseNewline($"Found potential CodeGenModules list at 0x{startOfCodegenModulesList:X} (ending at 0x{endOfCodegenModulesList:X}), with valid first module name {firstModule.Name}. Returning dummy code reg struct now!");

            //Now we can return a dummy CodeRegistration struct with the correct values.
            codereg = new() { codeGenModulesCount = (uint)moduleCount, addrCodeGenModulePtrs = startOfCodegenModulesList };

            return;
        }
    }

    private static ulong FindCodeGenModulesListFromMscorlib(Il2CppBinary binary, ulong pSomewhereInCodegenModules, ulong moduleCount, out ulong endOfCodegenModulesList)
    {
        //Unlike what BinarySearcher does now, we can't walk back and keep searching for references. But we know how many modules there are,
        //So let's now read *forward* one pointer at a time until we hopefully hit a null pointer.
        var pointerSize = binary.PointerSize;
        endOfCodegenModulesList = pSomewhereInCodegenModules + pointerSize;
        binary.Position = binary.MapVirtualAddressToRaw(endOfCodegenModulesList);

        while (binary.ReadNUint() != 0)
        {
            endOfCodegenModulesList += pointerSize;
        }

        //We're at the end, so walk one back to get the last valid pointer.
        endOfCodegenModulesList -= pointerSize;

        //Now subtract module count * pointer size to get the start of the list.
        var startOfCodegenModulesList = endOfCodegenModulesList - ((moduleCount - 1) * pointerSize);
        return startOfCodegenModulesList;
    }
}
