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

            Logger.VerboseNewline("\tRunning entire .text section through Arm64 disassembler, this might take a moment...");
            _allInstructions = disassembler.Disassemble(LibCpp2IlMain.Binary.GetEntirePrimaryExecutableSection(), (long)LibCpp2IlMain.Binary.GetVirtualAddressOfPrimaryExecutableSection()).ToList();
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
                        
                        if(addressesToIgnore.Contains((ulong) prevInstruction.Address))
                            continue;

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