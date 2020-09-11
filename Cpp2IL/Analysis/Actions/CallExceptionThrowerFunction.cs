using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class CallExceptionThrowerFunction : BaseAction
    {
        private static readonly Dictionary<ulong, TypeDefinition?> ExceptionThrowers = new Dictionary<ulong, TypeDefinition>();
        private TypeDefinition? _exceptionType;

        public static bool IsExceptionThrower(ulong addr, int recurseCount = 0)
        {
            if (recurseCount > 3) return false;

            if (ExceptionThrowers.ContainsKey(addr))
            {
                return ExceptionThrowers[addr] != null;
            }

            var body = Utils.GetMethodBodyAtVirtAddressNew(LibCpp2IlMain.ThePe, addr, true);
            var leas = body.Where(i => i.Mnemonic == Mnemonic.Lea).ToList();
            if (leas.Count > 1)
            {
                var strings = leas.Select(i => Utils.TryGetLiteralAt(LibCpp2IlMain.ThePe, (ulong) LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(i.GetRipBasedInstructionMemoryAddress()))).ToList();
                if (strings.All(s => s != null) && strings.Contains("System"))
                {
                    var exceptionName = strings[0];
                    var @namespace = strings[1];
                    var type = Utils.TryLookupTypeDefKnownNotGeneric(@namespace + "." + exceptionName);
                    if (type != null)
                    {
                        Console.WriteLine($"Identified direct exception thrower: 0x{addr:X} throws {type.FullName}");
                        ExceptionThrowers[addr] = type;
                        return true;
                    }
                }
            }

            //Only take first 3 calls.
            var calls = body.Where(i => i.Mnemonic == Mnemonic.Call || i.Mnemonic == Mnemonic.Jmp).Take(3).ToList();
            foreach (var instruction in calls)
            {
                var secondaryAddr = instruction.NearBranchTarget;
                if (IsExceptionThrower(secondaryAddr, recurseCount + 1))
                {
                    ExceptionThrowers[addr] = ExceptionThrowers[secondaryAddr];
                    Console.WriteLine($"Identified direct exception thrower: 0x{addr:X} throws {ExceptionThrowers[addr]?.FullName} because 0x{secondaryAddr:X} does.");
                    return true;
                }
            }

            //Mark not a thrower
            ExceptionThrowers[addr] = null;
            return false;
        }

        public CallExceptionThrowerFunction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var calledAddr = instruction.NearBranchTarget;
            _exceptionType = ExceptionThrowers[calledAddr];
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Constructs and throws an exception of kind {_exceptionType}\n";
        }
    }
}