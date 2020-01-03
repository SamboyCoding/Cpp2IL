using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    public struct KeyFunctionAddresses
    {
        public ulong AddrInitFunction;
        public ulong AddrBailOutFunction;
        public ulong AddrInitStaticFunction;
        public ulong AddrNewFunction;

        public static KeyFunctionAddresses Find(List<Tuple<TypeDefinition, List<CppMethodData>>> methodData, PE.PE cppAssembly)
        {
            var ret = new KeyFunctionAddresses();

            //First: The function that sets up a method, only needs to be located so we can ignore it.
            //Many easy places to get this, but ideally we need a CLR method without any overloads so it's not ambiguous
            
            //Chosen Target: ArgumentCache#TidyAssemblyTypeName
            //That's in UnityEngine.CoreModule.dll but close enough
            //I don't even know what that method does but whatever
            var methods = methodData.Find(t => t.Item1.Name == "ArgumentCache" && t.Item1.Namespace == "UnityEngine.Events").Item2;

            var tatn = methods.Find(m => m.MethodName == "TidyAssemblyTypeName");
            var instructions = Utils.DisassembleBytes(tatn.MethodBytes);
            
            var targetCall = instructions.First(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall);
            var addr = Utils.GetJumpTarget(targetCall, tatn.MethodOffsetRam + targetCall.PC);
            
            Console.WriteLine($"\t\tLocated Function Init function at 0x{addr:X}");
            ret.AddrInitFunction = addr;
            
            //Need to find the bail-out function, again so we can ignore it, as it's injected for null safety because cpp really doesn't like null pointer dereferences.
            //But it can be safely stripped out of IL - it'll just throw an NRE which is what this is a replacement for anyway.
            //Same function will do nicely.
            //These are always generated using the ASM `TEST RCX,RCX` folowed by `JZ [instruction which calls the function we want]`
            //So let's try to find a TEST RCX, RCX

            var targetTest = instructions.Find(insn => insn.Mnemonic == ud_mnemonic_code.UD_Itest && insn.Operands.Length == 2 && insn.Operands[0].Base == ud_type.UD_R_RCX && insn.Operands[1].Base == ud_type.UD_R_RCX);
            var targetJz = instructions[instructions.IndexOf(targetTest) + 1];
            if(targetJz.Mnemonic != ud_mnemonic_code.UD_Ijz) throw new Exception($"Failed detection of bailout function! TEST was not followed by JZ, but by {targetJz.Mnemonic}");

            var addrOfCall = Utils.GetJumpTarget(targetJz, tatn.MethodOffsetRam + targetJz.PC);
            
            //Get 5 bytes at that point so we can disasm
            //Warning: This might be fragile if the x86 instruction set ever changes.
            var bytes = cppAssembly.raw.SubArray((int) cppAssembly.MapVirtualAddressToRaw(addrOfCall), 5);
            var callInstruction = Utils.DisassembleBytes(bytes).First();

            addr = Utils.GetJumpTarget(callInstruction, addrOfCall + (ulong) bytes.Length);
            Console.WriteLine($"\t\tLocated Bailout function at 0x{addr:X}");
            ret.AddrBailOutFunction = addr;
            
            //Now we're on the "Init Static Class" one. Easiest place for this is in UnityEngine.Debug$$LogWarning
            methods = methodData.Find(t => t.Item1.Name == "Debug" && t.Item1.Namespace == "UnityEngine").Item2;
            
            //There are two of these but it doesn't matter which we get.
            var logWarn = methods.Find(m => m.MethodName == "LogWarning");
            instructions = Utils.DisassembleBytes(logWarn.MethodBytes);
            
            //Method: Find the second CALL as it points at what we want. (The first is the init method)
            var calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

            addr = Utils.GetJumpTarget(calls[1], logWarn.MethodOffsetRam + calls[1].PC);
            Console.WriteLine($"\t\tLocated Static Class Init function at 0x{addr:X}");
            ret.AddrInitStaticFunction = addr;
            
            //Find `new` function (note this is NOT the constructor) from System.Globalization.DateTimeFormatInfo's ctor
            methods = methodData.Find(t => t.Item1.Name == "DateTimeFormatInfo" && t.Item1.Namespace == "System.Globalization").Item2;

            var ctor = methods.Find(m => m.MethodName == ".ctor");
            instructions = Utils.DisassembleBytes(ctor.MethodBytes);
            
            //Once again just get the second call
            calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

            addr = Utils.GetJumpTarget(calls[1], ctor.MethodOffsetRam + calls[1].PC);
            Console.WriteLine($"\t\tLocated Class Instantiation (`new`) function at 0x{addr:X}");
            ret.AddrNewFunction = addr;

            //TODO: Would be good to have the lookupNativeMethod so we can pattern match and patch out.

            return ret;
        }
    }
}