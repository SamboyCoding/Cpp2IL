﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class CallExceptionThrowerFunction : AbstractExceptionThrowerAction<Instruction>
    {
        private static readonly ConcurrentDictionary<ulong, TypeDefinition?> ExceptionThrowers = new();

        internal static void Reset() => ExceptionThrowers.Clear();

        private static void CheckForExceptionThrower(ulong addr, int recurseCount)
        {
            if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(addr, out _))
            {
                ExceptionThrowers.TryAdd(addr, null);
                return;
            }

            var body = X86Utils.GetMethodBodyAtVirtAddressNew(addr, true);
            List<string?> strings;
            if (LibCpp2IlMain.Binary.is32Bit)
            {
                //Didn't know this, but in 32-bit assemblies, strings are immediate values? Interesting... not memory?
                strings = body.Where(i => i.Mnemonic == Mnemonic.Push && i.Op0Kind.IsImmediate())
                    .Select(i => LibCpp2IlMain.Binary.TryMapVirtualAddressToRaw(i.GetImmediate(0), out var raw) ? raw : long.MinValue)
                    .Where(l => l != long.MinValue)
                    .Select(pString => MiscUtils.TryGetLiteralAt(LibCpp2IlMain.Binary, (ulong) pString))
                    .Where(s => s != null)
                    .ToList()!; //Non-null asserted because we've just checked s is non-null.
            }
            else
            {
                var leas = body.Where(i => i.Mnemonic == Mnemonic.Lea).ToList();
                if (leas.Count > 1)
                {
                    //LEA to load strings in 64-bit mode
                    strings = leas
                        .Select(i => LibCpp2IlMain.Binary.TryMapVirtualAddressToRaw(i.MemoryDisplacement64, out var addr) ? addr : 0)
                        .Where(ptr => ptr != 0)
                        .Select(p => MiscUtils.TryGetLiteralAt(LibCpp2IlMain.Binary, (ulong) p))
                        .ToList();
                }
                else
                {
                    strings = new List<string?>();
                }
            }

            if (strings.All(s => s != null) && strings.Contains("System") && strings.Count > 1)
            {
                var exceptionName = strings[0];
                var @namespace = strings[1];
                var type = MiscUtils.TryLookupTypeDefKnownNotGeneric(@namespace + "." + exceptionName);
                if (type != null)
                {
                    Logger.VerboseNewline($"Identified direct exception thrower: 0x{addr:X} throws {type.FullName}", "Analyze");
                    ExceptionThrowers.TryAdd(addr, type);
                    return;
                }
            }

            //Only take first 3 calls.
            var calls = body.Where(i => i.Mnemonic == Mnemonic.Call || i.Mnemonic == Mnemonic.Jmp).Take(3).ToList();
            foreach (var instruction in calls)
            {
                var secondaryAddr = instruction.NearBranchTarget; //Can be zero if it's a jump into an imported function
                if (secondaryAddr != 0 && IsExceptionThrower(secondaryAddr, recurseCount + 1))
                {
                    ExceptionThrowers.TryAdd(addr, ExceptionThrowers[secondaryAddr]);
                    // Console.WriteLine($"Identified direct exception thrower: 0x{addr:X} throws {ExceptionThrowers[addr]?.FullName} because 0x{secondaryAddr:X} does.");
                    return;
                }
            }

            //Mark not a thrower
            ExceptionThrowers.TryAdd(addr, null);
        }

        public static bool IsExceptionThrower(ulong addr, int recurseCount = 0)
        {
            if (recurseCount > 4) 
                return false;

            if (!ExceptionThrowers.ContainsKey(addr)) 
                CheckForExceptionThrower(addr, recurseCount);

            return ExceptionThrowers[addr] != null;
        }

        public static TypeReference? GetExceptionThrown(ulong addr)
        {
            if (IsExceptionThrower(addr))
            {
                return ExceptionThrowers[addr];
            }

            return null;
        }

        public CallExceptionThrowerFunction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var calledAddr = instruction.NearBranchTarget;
            _exceptionType = ExceptionThrowers[calledAddr];
            
            if(_exceptionType != null)
                context.MakeLocal(_exceptionType, reg: "rax");
        }
    }
}