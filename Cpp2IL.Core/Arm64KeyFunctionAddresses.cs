using System;
using System.Collections.Generic;
using System.Linq;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;

namespace Cpp2IL.Core
{
    public class Arm64KeyFunctionAddresses : BaseKeyFunctionAddresses
    {
        private readonly List<Arm64Instruction> _allInstructions;

        public Arm64KeyFunctionAddresses()
        {
            var disassembler = CapstoneDisassembler.CreateArm64Disassembler(LibCpp2IlMain.Binary.IsBigEndian ? Arm64DisassembleMode.BigEndian : Arm64DisassembleMode.LittleEndian);
            disassembler.EnableInstructionDetails = true;
            disassembler.DisassembleSyntax = DisassembleSyntax.Intel;
            disassembler.EnableSkipDataMode = true;

            var primaryExecutableSection = LibCpp2IlMain.Binary.GetEntirePrimaryExecutableSection();
            var primaryExecutableSectionVa = LibCpp2IlMain.Binary.GetVirtualAddressOfPrimaryExecutableSection();
            var endOfTextSection = primaryExecutableSectionVa + (ulong)primaryExecutableSection.Length;

            Logger.InfoNewline("\tRunning entire .text section through Arm64 disassembler, this might take up to several minutes for large games, and may fail on large games if you have <16GB ram...");

            Logger.VerboseNewline($"\tPrimary executable section is {primaryExecutableSection.Length} bytes, starting at 0x{primaryExecutableSectionVa:X} and extending to 0x{endOfTextSection:X}");
            var attributeGeneratorList = SharedState.AttributeGeneratorStarts.ToList();
            attributeGeneratorList.SortByExtractedKey(a => a);
            
            Logger.VerboseNewline($"\tLast attribute generator function is at address 0x{attributeGeneratorList[^1]:X}. Skipping everything before that.");
            
            //Optimisation: We can skip all bytes up to and including the last attribute restoration function
            //However we don't know how long the last restoration function is, so just skip up to it, we'd only be saving a further 100 instructions or so
            //These come at the beginning of the .text section usually and the only thing that comes before them is unmanaged finalizers and initializers.
            //This may not be correct on v29 which uses the Bee compiler, which may do things differently
            var oldLength = primaryExecutableSection.Length;

            var toRemove = (int) (attributeGeneratorList[^1] - primaryExecutableSectionVa);
            primaryExecutableSection = primaryExecutableSection.Skip(toRemove).ToArray();

            primaryExecutableSectionVa = attributeGeneratorList[^1];
            
            Logger.VerboseNewline($"\tBy trimming out attribute generator functions, reduced decompilation work by {toRemove} of {oldLength} bytes (a {toRemove * 100 / (float) oldLength:f1}% saving)");
            
            //Some games (e.g. Muse Dash APK) contain the il2cpp-ified methods in the .text section instead of their own dedicated one.
            //That makes this very slow
            //Try and detect the first function
            var methodAddresses = SharedState.MethodsByAddress.Keys.Where(a => a > 0).ToList();
            methodAddresses.SortByExtractedKey(a => a);

            if (methodAddresses[0] < endOfTextSection)
            {
                var exportAddresses = new[]
                {
                    "il2cpp_object_new", "il2cpp_value_box", "il2cpp_runtime_class_init", "il2cpp_array_new_specific",
                    "il2cpp_type_get_object", "il2cpp_resolve_icall", "il2cpp_string_new", "il2cpp_string_new_wrapper",
                    "il2cpp_raise_exception"
                }.Select(LibCpp2IlMain.Binary.GetVirtualAddressOfExportedFunctionByName).Where(a => a > 0).ToArray();

                var lastExport = exportAddresses.Max();
                var firstExport = exportAddresses.Min();
                
                Logger.VerboseNewline($"\tDetected that the il2cpp-ified managed methods are in the .text section. Attempting to trim them out for KFA scanning - the first managed method is at 0x{methodAddresses[0]:X} and the last at 0x{methodAddresses[^1]:X}, " +
                                      $"the first export function is at 0x{firstExport:X} and the last at 0x{lastExport:X}");
                
                //I am assuming, arbitrarily, that the exports are always towards the end of the managed methods, in this case.
                var startFrom = Math.Min(firstExport, methodAddresses[^1]);
                
                //Just in case we didn't get the first export, let's subtract a little
                if (startFrom > 0x100_0000)
                    startFrom -= 0x10_0000;
                
                Logger.VerboseNewline($"\tTrimming everything before 0x{startFrom:X}.");
                oldLength = primaryExecutableSection.Length;
                
                toRemove = (int) (startFrom - primaryExecutableSectionVa);
                primaryExecutableSection = primaryExecutableSection.Skip(toRemove).ToArray();

                primaryExecutableSectionVa = startFrom;
            
                Logger.VerboseNewline($"\tBy trimming out most of the il2cpp-ified managed methods, reduced decompilation work by {toRemove} of {oldLength} bytes (a {toRemove * 100L / (float) oldLength:f1}% saving)");
            }
            

            _allInstructions = disassembler.Disassemble(primaryExecutableSection, (long)primaryExecutableSectionVa).ToList();
        }
        
        protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
        {
            var allBranchesToAddr = _allInstructions.Where(i => i.Mnemonic == "b")
                .Where(i => i.Details.Operands[0].IsImmediate() && i.Details.Operands[0].Immediate == (long)addr)
                .ToList();

            foreach (var potentialBranch in allBranchesToAddr)
            {
                if(addressesToIgnore.Contains((ulong) potentialBranch.Address))
                    continue;

                var backtrack = 0;
                var idx = _allInstructions.IndexOf(potentialBranch);

                do
                {
                    //About the only way we have of checking for a thunk is if there another unconditional branch just before this
                    //Or a ret, but that's less reliable.
                    //if so, this is probably a thunk.
                    if (idx - backtrack > 0)
                    {
                        var prevInstruction = _allInstructions[idx - backtrack - 1];

                        if (addressesToIgnore.Contains((ulong)prevInstruction.Address))
                        {
                            backtrack++;
                            continue;
                        }

                        if (prevInstruction.Mnemonic is "b" or "bl")
                        {
                            yield return (ulong)potentialBranch.Address - (ulong)(backtrack * 4);
                            break;
                        }

                        if (prevInstruction.Mnemonic is "ret")
                        {
                            yield return (ulong)potentialBranch.Address - (ulong)(backtrack * 4);
                            break;
                        }
                    }
                    
                    //We're working in the .text section here so we have few symbols, so there's no point looking for the previous one.

                    backtrack++;
                } while (backtrack * 4 < maxBytesBack);
            }
        }

        protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
        {
            var idx = _allInstructions.FindIndex(i => i.Address == (long)thunkPtr);

            //Easy case, we have an unconditional jump at that address, just return what it points at
            if (_allInstructions[idx].Mnemonic is "b" or "bl")
                return (ulong)_allInstructions[idx].Details.Operands[0].Immediate;
            
            //Max number of instructions to check is 12. I use this because we check 50 bytes in x86 and 4 * 12 is 48.

            for (var i = 0; i <= 12; i++)
            {
                idx++;
                if (_allInstructions[idx].Mnemonic is "b" or "bl")
                    return (ulong)_allInstructions[idx].Details.Operands[0].Immediate;
            }

            return 0;
        }

        protected override int GetCallerCount(ulong toWhere) => _allInstructions.Where(i => i.Mnemonic is "b" or "bl")
            .Count(i => i.Details.Operands[0].IsImmediate() && i.Details.Operands[0].Immediate == (long)toWhere);
    }
}