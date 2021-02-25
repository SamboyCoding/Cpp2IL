using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallExceptionThrowerFunction : BaseAction
    {
        private static readonly Dictionary<ulong, TypeDefinition?> ExceptionThrowers = new Dictionary<ulong, TypeDefinition>();
        private TypeDefinition? _exceptionType;

        public static bool IsExceptionThrower(ulong addr, int recurseCount = 0)
        {
            if (recurseCount > 4) return false;

            if (ExceptionThrowers.ContainsKey(addr))
            {
                return ExceptionThrowers[addr] != null;
            }

            var body = Utils.GetMethodBodyAtVirtAddressNew(LibCpp2IlMain.ThePe, addr, true);
            List<string> strings;
            if (LibCpp2IlMain.ThePe.is32Bit)
            {
                //Didn't know this, but in 32-bit assemblies, strings are immediate values? Interesting... not memory?
                strings = body.Where(i => i.Mnemonic == Mnemonic.Push && i.Op0Kind.IsImmediate())
                    .Select(i => Utils.TryGetLiteralAt(LibCpp2IlMain.ThePe, (ulong) LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(i.GetImmediate(0))))
                    .Where(s => s != null)
                    .ToList();
            }
            else
            {
                var leas = body.Where(i => i.Mnemonic == Mnemonic.Lea).ToList();
                if (leas.Count > 1)
                {
                    //LEA to load strings in 64-bit mode
                    strings = leas.Select(i => Utils.TryGetLiteralAt(LibCpp2IlMain.ThePe, (ulong) LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(i.GetRipBasedInstructionMemoryAddress()))).ToList();
                }
                else
                {
                    strings = new List<string>();
                }
            }

            if (strings.All(s => s != null) && strings.Contains("System") && strings.Count > 1)
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

            //Only take first 3 calls.
            var calls = body.Where(i => i.Mnemonic == Mnemonic.Call || i.Mnemonic == Mnemonic.Jmp).Take(3).ToList();
            foreach (var instruction in calls)
            {
                var secondaryAddr = instruction.NearBranchTarget; //Can be zero if it's a jump into an imported function
                if (secondaryAddr != 0 && IsExceptionThrower(secondaryAddr, recurseCount + 1))
                {
                    ExceptionThrowers[addr] = ExceptionThrowers[secondaryAddr];
                    // Console.WriteLine($"Identified direct exception thrower: 0x{addr:X} throws {ExceptionThrowers[addr]?.FullName} because 0x{secondaryAddr:X} does.");
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
            
            if(_exceptionType != null)
                context.MakeLocal(_exceptionType, reg: "rax");
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"throw new {_exceptionType}()";
        }

        public override string ToTextSummary()
        {
            return $"[!] Constructs and throws an exception of kind {_exceptionType}\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}