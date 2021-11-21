using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using LibCpp2IL.NintendoSwitch;

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
            var endOfTextSection = primaryExecutableSectionVa + (ulong) primaryExecutableSection.Length;

            Logger.InfoNewline("\tRunning entire .text section through Arm64 disassembler, this might take up to several minutes for large games, and may fail on large games if you have <16GB ram...");

            Logger.VerboseNewline($"\tPrimary executable section is {primaryExecutableSection.Length} bytes, starting at 0x{primaryExecutableSectionVa:X} and extending to 0x{endOfTextSection:X}");
            var attributeGeneratorList = SharedState.AttributeGeneratorStarts.ToList();
            attributeGeneratorList.SortByExtractedKey(a => a);

            if (LibCpp2IlMain.Binary is not NsoFile)
            {
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

                if (methodAddresses[0] < endOfTextSection && LibCpp2IlMain.Binary.GetVirtualAddressOfExportedFunctionByName("il2cpp_object_new") != 0)
                {
                    var exportAddresses = new[]
                    {
                        "il2cpp_object_new", "il2cpp_value_box", "il2cpp_runtime_class_init", "il2cpp_array_new_specific",
                        "il2cpp_type_get_object", "il2cpp_resolve_icall", "il2cpp_string_new", "il2cpp_string_new_wrapper",
                        "il2cpp_raise_exception"
                    }.Select(LibCpp2IlMain.Binary.GetVirtualAddressOfExportedFunctionByName).Where(a => a > 0).ToArray();

                    var lastExport = exportAddresses.Max();
                    var firstExport = exportAddresses.Min();

                    Logger.VerboseNewline($"\tDetected that the il2cpp-ified managed methods are in the .text section and api functions are available. Attempting to trim out managed methods for KFA scanning - the first managed method is at 0x{methodAddresses[0]:X} and the last at 0x{methodAddresses[^1]:X}, " +
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
                else if (methodAddresses[0] < endOfTextSection)
                {
                    Logger.VerboseNewline($"\tDetected that the il2cpp-ified managed methods are in the .text section, but api functions are not available. Attempting to (conservatively) trim out managed methods for KFA scanning - the first managed method is at 0x{methodAddresses[0]:X} and the last at 0x{methodAddresses[^1]:X}");

                    var startFrom = methodAddresses[^1];

                    //Just in case the exports are mixed in with the end of the managed methods, let's subtract a little
                    if (startFrom > 0x100_0000)
                        startFrom -= 0x10_0000;

                    Logger.VerboseNewline($"\tTrimming everything before 0x{startFrom:X}.");
                    oldLength = primaryExecutableSection.Length;

                    toRemove = (int) (startFrom - primaryExecutableSectionVa);
                    primaryExecutableSection = primaryExecutableSection.Skip(toRemove).ToArray();

                    primaryExecutableSectionVa = startFrom;

                    Logger.VerboseNewline($"\tBy trimming out most of the il2cpp-ified managed methods, reduced decompilation work by {toRemove} of {oldLength} bytes (a {toRemove * 100L / (float) oldLength:f1}% saving)");
                }
            }
            else
            {
                //For now we skip everything after the last attribute generator. Not sure this is always reliable but in test binaries it works.
                //We choose last not first to include all the generators, so that we hopefully have some context for api function detection.
                Logger.VerboseNewline($"\tNSO: Last attribute generator function is at address 0x{attributeGeneratorList[^1]:X}. Skipping everything after that.");
                
                var oldLength = primaryExecutableSection.Length;

                var toKeep = (int) (attributeGeneratorList[^1] - primaryExecutableSectionVa);
                primaryExecutableSection = primaryExecutableSection.SubArray(..toKeep);

                //This doesn't change, we've trimmed the end, not the beginning
                // primaryExecutableSectionVa = primaryExecutableSectionVa;

                Logger.VerboseNewline($"\tBy trimming out everything after and including attribute generator functions, reduced decompilation work by {oldLength-toKeep} of {oldLength} bytes (a {(oldLength-toKeep) * 100L / (float) oldLength:f1}% saving)");
            }

            _allInstructions = disassembler.Disassemble(primaryExecutableSection, (long) primaryExecutableSectionVa).ToList();
        }

        protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
        {
            var allBranchesToAddr = _allInstructions.Where(i => i.Mnemonic is "b" or "bl")
                .Where(i => i.Details.Operands[0].IsImmediate() && i.Details.Operands[0].Immediate == (long) addr)
                .ToList();

            foreach (var potentialBranch in allBranchesToAddr)
            {
                if (addressesToIgnore.Contains((ulong) potentialBranch.Address))
                    continue;

                var backtrack = 0;
                var idx = _allInstructions.IndexOf(potentialBranch);

                do
                {
                    //About the only way we have of checking for a thunk is if there is an all-zero instruction or another unconditional branch just before this
                    //Or a ret, but that's less reliable.
                    //if so, this is probably a thunk.
                    if (idx - backtrack > 0)
                    {
                        var prevInstruction = _allInstructions[idx - backtrack - 1];

                        if (addressesToIgnore.Contains((ulong) prevInstruction.Address))
                        {
                            backtrack++;
                            continue;
                        }

                        if (prevInstruction.IsSkippedData && prevInstruction.Bytes.All(b => b == 0))
                        {
                            //All-zero instructions are a match
                            yield return (ulong) potentialBranch.Address - (ulong) (backtrack * 4);
                            break;
                        }

                        if (prevInstruction.Mnemonic is "adrp" or "str")
                        {
                            //ADRP instructions are a deal breaker - this means we're loading something from memory, so it's not a simple thunk
                            break;
                        }
                        
                        if (prevInstruction.Mnemonic is "b" or "bl")
                        {
                            //Previous branches are a match
                            yield return (ulong) potentialBranch.Address - (ulong) (backtrack * 4);
                            break;
                        }

                        if (prevInstruction.Mnemonic is "ret")
                        {
                            //Previous rets are a match
                            yield return (ulong) potentialBranch.Address - (ulong) (backtrack * 4);
                            break;
                        }
                    }

                    //We're working in the .text section here so we have few symbols, so there's no point looking for the previous one.

                    backtrack++;
                } while (backtrack * 4 < maxBytesBack);
            }
        }

        protected override ulong GetObjectIsInstFromSystemType()
        {
            Logger.Verbose("\tTrying to use System.Type::IsInstanceOfType to find il2cpp::vm::Object::IsInst...");
            var typeIsInstanceOfType = LibCpp2IlReflection.GetType("Type", "System")?.Methods?.FirstOrDefault(m => m.Name == "IsInstanceOfType");
            if (typeIsInstanceOfType == null)
            {
                Logger.VerboseNewline("Type or method not found, aborting.");
                return 0;
            }
            
            //IsInstanceOfType is a very simple ICall, that looks like this:
            //  Il2CppClass* klass = vm::Class::FromIl2CppType(type->type.type);
            //  return il2cpp::vm::Object::IsInst(obj, klass) != NULL;
            //The last call is to Object::IsInst
            
            Logger.Verbose($"IsInstanceOfType found at 0x{typeIsInstanceOfType.MethodPointer:X}...");
            var instructions = Arm64Utils.GetArm64MethodBodyAtVirtualAddress(typeIsInstanceOfType.MethodPointer, false);

            var lastCall = instructions.LastOrDefault(i => i.Mnemonic == "bl");

            if (lastCall == null)
            {
                Logger.VerboseNewline("Method does not match expected signature. Aborting.");
                return 0;
            }
            
            Logger.VerboseNewline($"Success. IsInst found at 0x{lastCall.Details.Operands[0].Immediate:X}");
            return (ulong) lastCall.Details.Operands[0].Immediate;            
        }

        protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
        {
            var idx = _allInstructions.FindIndex(i => i.Address == (long) thunkPtr);

            if (idx < 0)
                return 0;

            //Easy case, we have an unconditional jump at that address, just return what it points at
            if (_allInstructions[idx].Mnemonic is "b" or "bl")
                return (ulong) _allInstructions[idx].Details.Operands[0].Immediate;

            //Max number of instructions to check is 12. I use this because we check 50 bytes in x86 and 4 * 12 is 48.

            for (var i = 0; i <= 12; i++)
            {
                idx++;
                if (_allInstructions[idx].Mnemonic is "b" or "bl")
                    return (ulong) _allInstructions[idx].Details.Operands[0].Immediate;
            }

            return 0;
        }

        protected override int GetCallerCount(ulong toWhere) => _allInstructions.Where(i => i.Mnemonic is "b" or "bl")
            .Count(i => i.Details.Operands[0].IsImmediate() && i.Details.Operands[0].Immediate == (long) toWhere);

        protected override void AttemptInstructionAnalysisToFillGaps()
        {
            Logger.Verbose("\tAttempting to use Array GetEnumerator to find il2cpp_codegen_object_new...");
            if (TypeDefinitions.Array is { } arrayTypeDef)
            {
                if (arrayTypeDef.Methods.FirstOrDefault(m => m.Name == "GetEnumerator") is { } methodDef)
                {
                    var ptr = methodDef.AsUnmanaged().MethodPointer;
                    var body = Arm64Utils.GetArm64MethodBodyAtVirtualAddress(ptr, true);

                    //Looking for adrp, ldr, ldr, bl. Probably more than one - the first will be initializing the method, second will be the constructor call
                    var probableResult = 0L;
                    var numSeen = 0;
                    for (var i = 0; i < body.Count - 4; i++)
                    {
                        if (body[i].Mnemonic != "adrp" || body[i + 1].Mnemonic != "ldr" || body[i + 2].Mnemonic != "ldr" || body[i + 3].Mnemonic != "bl")
                            continue;

                        if (numSeen++ < 2) //Only store first one or second one
                            probableResult = body[i + 3].Details.Operands[0].Immediate;
                    }

                    if (probableResult > 0)
                    {
                        Logger.Verbose($"Probably found at 0x{probableResult:X}...");

                        //This is *codegen*_object_new. Probably. Check it
                        var thunk = FindFunctionThisIsAThunkOf((ulong) probableResult);
                        long addrVmObjectNew;
                        if (thunk > 0)
                            //We've found codegen_object_new, map to vm::Object::New, then try and get back to object_new
                            addrVmObjectNew = (long) thunk;
                        else
                            //Looks like we've been inlined and this is just vm::Object::New.
                            addrVmObjectNew = probableResult;

                        var allThunks = FindAllThunkFunctions((ulong) addrVmObjectNew, 16, (ulong) probableResult).ToList();
                        
                        allThunks.SortByExtractedKey(GetCallerCount); //Sort in ascending order by caller count
                        allThunks.Reverse(); //Reverse to be in descending order

                        il2cpp_object_new = allThunks.FirstOrDefault(); //Take first (i.e. most called)
                        
                        Logger.VerboseNewline($"Leaving il2cpp_object_new at 0x{il2cpp_object_new:X}");
                    }
                    else
                        Logger.VerboseNewline("Couldn't find a matching instruction, can't be used.");
                }
                else
                    Logger.VerboseNewline("Method stripped, can't be used.");
            }
            else
                Logger.VerboseNewline("Type stripped, can't be used.");
        }
    }
}