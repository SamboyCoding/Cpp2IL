using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64CallThrowHelperAction : AbstractExceptionThrowerAction<Arm64Instruction>
    {
        private static readonly ConcurrentDictionary<long, TypeDefinition> _exceptionsThrownByAddress = new();
        private static readonly List<long> _checkedAddresses = new();

        public static bool IsThrowHelper(long pointer, int depth = 0)
        {
            if (depth >= 5)
                return false;

            if (_exceptionsThrownByAddress.ContainsKey(pointer))
                return true;

            if (_checkedAddresses.Contains(pointer))
                return false;
            
            _checkedAddresses.Add(pointer);

            //This will only return up to the first branch, because it's an unmanaged function, but that's fine for these purposes
            var funcBody = Utils.Utils.GetArm64MethodBodyAtVirtualAddress((ulong)pointer, false, 14);

            var registerPages = new Dictionary<string, long>();
            foreach (var arm64Instruction in funcBody.Where(i => i.Mnemonic is "adrp" && i.Details.Operands[0].Type == Arm64OperandType.Register))
            {
                registerPages[arm64Instruction.Details.Operands[0].Register.Name.ToLowerInvariant()] = arm64Instruction.Details.Operands[1].Immediate;
            }

            var registerAddresses = new Dictionary<string, long>();
            foreach (var arm64Instruction in funcBody.Where(i => i.Mnemonic is "add" && i.Details.Operands.Length == 3))
            {
                var regName = arm64Instruction.Details.Operands[1].RegisterSafe()?.Name;
                if (regName != null && registerPages.TryGetValue(regName, out var page) && arm64Instruction.Details.Operands[2].IsImmediate())
                {
                    var destName = arm64Instruction.Details.Operands[0].RegisterSafe()?.Name;
                    registerAddresses[destName ?? "invalid"] = page + arm64Instruction.Details.Operands[2].Immediate;
                }
            }
            
            foreach (var potentialLiteralAddress in registerAddresses.Values)
            {
                if (Utils.Utils.TryGetLiteralAt(LibCpp2IlMain.Binary!, (ulong)LibCpp2IlMain.Binary!.MapVirtualAddressToRaw((ulong)potentialLiteralAddress)) is not { } literal) 
                    continue;
                if (Utils.Utils.TryLookupTypeDefKnownNotGeneric($"System.{literal}") is not { } exceptionType)
                    continue;
                    
                Logger.VerboseNewline($"Identified direct exception thrower: 0x{pointer:X} throws {exceptionType.FullName}. Instructions were {string.Join(", ", funcBody.Select(i => $"0x{i.Address:X} {i.Mnemonic}"))}", "Analyze");
                _exceptionsThrownByAddress.TryAdd(pointer, exceptionType);
                return true;
            }

            //Check for inherited exception throwers.
            foreach (var nextPtr in 
                from i in funcBody
                where i.Mnemonic is "b" or "bl" && i.Details.Operands[0].IsImmediate() 
                select i.Details.Operands[0].Immediate 
                into nextPtr 
                where IsThrowHelper(nextPtr, depth + 1) 
                select nextPtr)
            {
                _exceptionsThrownByAddress.TryAdd(pointer, _exceptionsThrownByAddress[nextPtr]);
                return true;
            }

            return false;
        }

        public static TypeDefinition? GetExceptionThrown(long ptr) => _exceptionsThrownByAddress.TryGetValue(ptr, out var ex) ? ex : null; 

        public Arm64CallThrowHelperAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            var functionAddress = instruction.Details.Operands[0].Immediate;
            _exceptionType = _exceptionsThrownByAddress[functionAddress];
        }
    }
}