using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.PE;

namespace Cpp2IL.Core
{
    public class X86KeyFunctionAddresses : BaseKeyFunctionAddresses
    {
        protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
        {
            //Disassemble .text
            var allInstructions = ((PE) LibCpp2IlMain.Binary!).DisassembleTextSection();

            //Find all jumps to the target address
            var matchingJmps = allInstructions.Where(i => i.Mnemonic is Mnemonic.Jmp or Mnemonic.Call && i.NearBranchTarget == addr).ToList();

            foreach (var matchingJmp in matchingJmps)
            {
                if (addressesToIgnore.Contains(matchingJmp.IP)) continue;

                //Find this instruction in the raw file
                var offsetInPe = (ulong) LibCpp2IlMain.Binary.MapVirtualAddressToRaw(matchingJmp.IP);
                if (offsetInPe == 0 || offsetInPe == (ulong) (LibCpp2IlMain.Binary!.RawLength - 1))
                    continue;

                //get next and previous bytes
                var previousByte = LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe - 1);
                var nextByte = LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe + (ulong) matchingJmp.Length);

                //Double-cc = thunk
                if (previousByte == 0xCC && nextByte == 0xCC)
                {
                    yield return matchingJmp.IP;
                    continue;
                }

                if (nextByte == 0xCC && maxBytesBack > 0)
                {
                    for (ulong backtrack = 1; backtrack < maxBytesBack && offsetInPe - backtrack > 0; backtrack++)
                    {
                        if (addressesToIgnore.Contains(matchingJmp.IP - (backtrack - 1)))
                            //Move to next jmp
                            break;

                        if (LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe - backtrack) == 0xCC)
                        {
                            yield return matchingJmp.IP - (backtrack - 1);
                            break;
                        }
                    }
                }
            }
        }

        protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
        {
            var instructions = Utils.Utils.GetMethodBodyAtVirtAddressNew(thunkPtr, true);

            try
            {
                var target = prioritiseCall ? Mnemonic.Call : Mnemonic.Jmp;
                var matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);

                if (matchingCall.Mnemonic == Mnemonic.INVALID)
                {
                    target = target == Mnemonic.Call ? Mnemonic.Jmp : Mnemonic.Call;
                    matchingCall = instructions.First(i => i.Mnemonic == target);
                }

                return matchingCall.NearBranchTarget;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        protected override int GetCallerCount(ulong toWhere)
        {
            //Disassemble .text
            var allInstructions = ((PE) LibCpp2IlMain.Binary!).DisassembleTextSection();

            //Find all jumps to the target address
            return allInstructions.Count(i => i.Mnemonic == Mnemonic.Jmp || i.Mnemonic == Mnemonic.Call && i.NearBranchTarget == toWhere);
        }
    }
}