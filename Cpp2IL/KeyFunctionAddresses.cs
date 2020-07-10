using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    public class KeyFunctionAddresses
    {
        public ulong il2cpp_codegen_initialize_method;
        public ulong il2cpp_vm_object_new;
        public ulong AddrBailOutFunction;
        public ulong il2cpp_runtime_class_init;
        public ulong il2cpp_codegen_object_new;
        public ulong AddrArrayCreation;
        public ulong AddrNativeLookup;
        public ulong AddrNativeLookupGenMissingMethod;
        public ulong AddrBoxValueMethod;
        public ulong AddrSafeCastMethod;
        public ulong il2cpp_raise_managed_exception;
        public ulong AddrPInvokeLookup;

        public static KeyFunctionAddresses Find(List<(TypeDefinition type, List<CppMethodData> methods)> methodData, PE.PE cppAssembly)
        {
            var ret = new KeyFunctionAddresses();

            //First: The function that sets up a method, only needs to be located so we can ignore it.
            //Many easy places to get this, but ideally we need a CLR method without any overloads so it's not ambiguous

            //Chosen Target: ArgumentCache#TidyAssemblyTypeName
            //That's in UnityEngine.CoreModule.dll but close enough
            //I don't even know what that method does but whatever
            
            Console.WriteLine("\t\tSearching for methods known to contain certain calls...");
            
            //Try to find System.Array/ArrayEnumerator
            TryUseArrayEnumerator(methodData, ret);
            
            //TODO: move everything else to this ^ format so we can pick-and-choose which structs we need 
            
            Console.WriteLine("\t\t\tLooking for UnityEngine.Events.ArgumentCache$TidyAssemblyTypeName...");

            var methods = methodData.Find(t => t.type.Name == "ArgumentCache" && t.type.Namespace == "UnityEngine.Events").methods;

            var tatn = methods.Find(m => m.MethodName == "TidyAssemblyTypeName");

            Console.WriteLine($"\t\t\t\tSearching for a call to il2cpp_codegen_initialize_method near offset 0x{tatn.MethodOffsetRam:X}...");

            var instructions = Utils.DisassembleBytes(tatn.MethodBytes);

            var targetCall = instructions.First(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall);
            var addr = Utils.GetJumpTarget(targetCall, tatn.MethodOffsetRam + targetCall.PC);

            Console.WriteLine($"\t\t\t\tLocated il2cpp_codegen_initialize_method function at 0x{addr:X}");
            ret.il2cpp_codegen_initialize_method = addr;

            //Need to find the bail-out function, again so we can ignore it, as it's injected for null safety because cpp really doesn't like null pointer dereferences.
            //But it can be safely stripped out of IL - it'll just throw an NRE which is what this is a replacement for anyway.
            //Same function will do nicely.
            //These are always generated using the ASM `TEST RCX,RCX` folowed by `JZ [instruction which calls the function we want]`
            //So let's try to find a TEST RCX, RCX

            Console.WriteLine($"\t\t\t\tSearching for a call to the generic bailout function near offset 0x{tatn.MethodOffsetRam:X}...");

            var targetTest = instructions.Find(insn => insn.Mnemonic == ud_mnemonic_code.UD_Itest && insn.Operands.Length == 2 && insn.Operands[0].Base == ud_type.UD_R_RCX && insn.Operands[1].Base == ud_type.UD_R_RCX);
            var targetJz = instructions[instructions.IndexOf(targetTest) + 1];
            if (targetJz.Mnemonic != ud_mnemonic_code.UD_Ijz)
            {
                Console.WriteLine($"Failed detection of bailout function! TEST was not followed by JZ, but by {targetJz.Mnemonic}");
            }
            else
            {
                var addrOfCall = Utils.GetJumpTarget(targetJz, tatn.MethodOffsetRam + targetJz.PC);

                //Get 5 bytes at that point so we can disasm
                //Warning: This might be fragile if the x86 instruction set ever changes.
                var bytes = cppAssembly.raw.SubArray((int) cppAssembly.MapVirtualAddressToRaw(addrOfCall), 5);
                var callInstruction = Utils.DisassembleBytes(bytes).First();

                addr = Utils.GetJumpTarget(callInstruction, addrOfCall + (ulong) bytes.Length);
                Console.WriteLine($"\t\t\t\tLocated Bailout function at 0x{addr:X}");
                ret.AddrBailOutFunction = addr;
            }

            //Now we're on the "Init Static Class" one. Easiest place for this is in UnityEngine.Debug$$LogWarning
            Console.WriteLine("\t\t\tLooking for UnityEngine.Debug$LogWarning...");
            methods = methodData.Find(t => t.type.Name == "Debug" && t.type.Namespace == "UnityEngine").methods;

            //There are two of these but it doesn't matter which we get.
            var logWarn = methods.Find(m => m.MethodName == "LogWarning");

            Console.WriteLine($"\t\t\t\tSearching for a call to il2cpp_runtime_class_init near offset 0x{logWarn.MethodOffsetRam:X}...");

            instructions = Utils.DisassembleBytes(logWarn.MethodBytes);

            //Method: Find the second CALL as it points at what we want. (The first is the init method)
            var calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

            addr = Utils.GetJumpTarget(calls[1], logWarn.MethodOffsetRam + calls[1].PC);
            Console.WriteLine($"\t\t\t\tLocated il2cpp_runtime_class_init at 0x{addr:X}");
            ret.il2cpp_runtime_class_init = addr;

            CppMethodData method;

            //Only if we haven't already found CON
            if (ret.il2cpp_codegen_object_new == 0)
            {
                Console.WriteLine("\t\t\tAttempting to locate il2cpp_codegen_object_new. Looking for System.Security.Cryptography.X509Certificates.X509Extension$FormatUnkownData (yes, there's a typo)...");
                //Find il2cpp_codegen_object_new (note this is NOT the constructor) from System.Security.Cryptography.X509Certificates.X509Extension::FormatUnkownData (yes, there's a typo in the method name lol)
                //We were using DateTimeFormatInfo but that's not constant between mono versions - this is.
                methods = methodData.Find(t => t.type.Name == "X509Extension" && t.type.Namespace == "System.Security.Cryptography.X509Certificates").methods;

                if (methods != null)
                {
                    method = methods.Find(m => m.MethodName == "FormatUnkownData"); //Yes, there's a typo

                    Console.WriteLine($"\t\t\t\tSearching for a call to il2cpp_codegen_object_new near offset 0x{method.MethodOffsetRam:X}...");

                    instructions = Utils.DisassembleBytes(method.MethodBytes);

                    //Once again just get the second call
                    calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

                    if (calls.Length > 1)
                    {

                        addr = Utils.GetJumpTarget(calls[1], method.MethodOffsetRam + calls[1].PC);
                        Console.WriteLine($"\t\t\t\tLocated il2cpp_codegen_object_new at 0x{addr:X}");

                        ret.il2cpp_codegen_object_new = addr;
                    }
                }
                if(ret.il2cpp_codegen_object_new == 0)
                {
                    Console.WriteLine("\t\t\tWarning: Failed to use X509Extension class, attempting to use fallback tether (System.Globalization.DateTimeFormatInfo$.ctor)...");
                    methods = methodData.Find(t => t.Item1.Name == "DateTimeFormatInfo" && t.Item1.Namespace == "System.Globalization").methods;

                    if (methods != null)
                    {
                        var ctor = methods.Find(m => m.MethodName == ".ctor");
                        Console.WriteLine($"\t\t\t\tSearching for class instantiation function near offset 0x{ctor.MethodOffsetRam:X}...");

                        instructions = Utils.DisassembleBytes(ctor.MethodBytes);
                        calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

                        addr = Utils.GetJumpTarget(calls[1], ctor.MethodOffsetRam + calls[1].PC);
                        Console.WriteLine($"\t\t\t\tLocated Class Instantiation (`new`) function at 0x{addr:X}");
                        ret.il2cpp_codegen_object_new = addr;
                    }
                    else
                    {
                        Console.WriteLine("\t\t\tError: Failed to find both X509Extension AND DateTimeFormatInfo, `new` calls will not be resolved!");
                    }
                }
            }

            Console.WriteLine("\t\t\tLooking for System.BitConverter$GetBytes...");
            //Find new[] using BitConverter
            methods = methodData.Find(t => t.type.Name == "BitConverter" && t.type.Namespace == "System").methods;

            method = methods.Find(m => m.MethodName == "GetBytes");

            Console.WriteLine($"\t\t\t\tSearching for array instantiation function near offset 0x{method.MethodOffsetRam:X}...");

            instructions = Utils.DisassembleBytes(method.MethodBytes);

            //Once again just get the second call
            calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

            addr = Utils.GetJumpTarget(calls[1], method.MethodOffsetRam + calls[1].PC);
            Console.WriteLine($"\t\t\t\tLocated Array Instantiation (`new[]`) function at 0x{addr:X}");

            ret.AddrArrayCreation = addr;

            methods = methodData.Find(t => t.type.Name == "Mesh" && t.type.Namespace == "UnityEngine").methods;

            method = methods.Find(m => m.MethodName == ".ctor");

            Console.WriteLine($"\t\t\t\tSearching for calls to the native lookup and bailout functions near offset 0x{method.MethodOffsetRam:X}...");

            instructions = Utils.DisassembleBytes(method.MethodBytes);

            calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

            if (calls.Length > 3)
            {
                addr = Utils.GetJumpTarget(calls[2], method.MethodOffsetRam + calls[2].PC);
                Console.WriteLine($"\t\t\t\tLocated Native Method Lookup function at 0x{addr:X}");
                ret.AddrNativeLookup = addr;

                addr = Utils.GetJumpTarget(calls[3], method.MethodOffsetRam + calls[3].PC);
                Console.WriteLine($"\t\t\t\tLocated Native Method Bailout function at 0x{addr:X}");
                ret.AddrNativeLookupGenMissingMethod = addr;
            }
            else
            {
                Console.WriteLine("\t\t\t\tWARNING: Failed to find native method lookup + bailout due to an insufficient number of CALL instructions.");
            }

            Console.WriteLine("\t\t\tLooking for System.ComponentModel.Int16Converter$ConvertFromString...");
            methods = methodData.Find(t => t.type.Name == "Int16Converter" && t.type.Namespace == "System.ComponentModel").methods;

            if (methods != null)
            {
                method = methods.Find(m => m.MethodName == "ConvertFromString");
                if (method.MethodOffsetRam == 0)
                    method = methods.Find(m => m.MethodName == "FromString");

                if (method.MethodOffsetRam != 0)
                {

                    Console.WriteLine($"\t\t\t\tSearching for primitive boxing function near offset 0x{method.MethodOffsetRam:X}...");

                    instructions = Utils.DisassembleBytes(method.MethodBytes);

                    calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

                    addr = Utils.GetJumpTarget(calls[2], method.MethodOffsetRam + calls[2].PC);
                    Console.WriteLine($"\t\t\t\tLocated Primitive Boxing function at 0x{addr:X}");

                    ret.AddrBoxValueMethod = addr;
                }
                else
                {
                    Console.WriteLine("\t\t\t\tWarning: Failed to locate method ConvertFromString / FromString in System.ComponentModel.Int16Converter (probably stripped from assembly), box statements will show as undefined function calls!");
                }
            }
            else
            {
                Console.WriteLine("\t\t\t\tWarning: Failed to locate System.ComponentModel.Int16Converter (probably stripped from assembly), box statements will show as undefined function calls!");
            }

            Console.WriteLine("\t\t\tLooking for UnityEngine.Ray$ToString...");
            methods = methodData.Find(t => t.type.Name == "Ray" && t.type.Namespace == "UnityEngine").methods;
            method = methods.Find(m => m.MethodName == "ToString");

            Console.WriteLine($"\t\t\t\tSearching for a call to the safe cast function near offset 0x{method.MethodOffsetRam:X}...");

            instructions = Utils.DisassembleBytes(method.MethodBytes);

            calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

            addr = Utils.GetJumpTarget(calls[3], method.MethodOffsetRam + calls[3].PC);
            Console.WriteLine($"\t\t\t\tLocated Safe Cast function at 0x{addr:X}");
            ret.AddrSafeCastMethod = addr;

            if (ret.il2cpp_raise_managed_exception == 0)
            {
                Console.WriteLine("\t\t\tLooking for System.RuntimeFieldHandle$GetObjectData...");
                methods = methodData.Find(t => t.type.Name == "RuntimeFieldHandle" && t.type.Namespace == "System").methods;
                method = methods.Find(m => m.MethodName == "GetObjectData");

                Console.WriteLine($"\t\t\t\tSearching for a call to System.ArgumentException$.ctor near offset 0x{method.MethodOffsetRam:X}...");

                instructions = Utils.DisassembleBytes(method.MethodBytes);

                calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

                var indexBefore = calls.ToList().FindIndex(call =>
                {
                    var targetAddrVirt = Utils.GetJumpTarget(call, method.MethodOffsetRam + call.PC);
                    if (SharedState.MethodsByAddress.TryGetValue(targetAddrVirt, out var methodDef))
                    {
                        if (methodDef.Name == ".ctor" && methodDef.DeclaringType.Name == "ArgumentException")
                            return true;
                    }

                    return false;
                });

                if (indexBefore >= 0)
                {
                    Console.WriteLine($"\t\t\t\tUsing ArgumentException construction to find il2cpp_raise_managed_exception, expecting it to be call ${indexBefore + 1}...");
                    addr = Utils.GetJumpTarget(calls[indexBefore + 1], method.MethodOffsetRam + calls[indexBefore + 1].PC);
                    Console.WriteLine($"\t\t\t\tLocated il2cpp_raise_managed_exception at 0x{addr:X}");
                    ret.il2cpp_raise_managed_exception = addr;
                }
                else
                {
                    Console.WriteLine("ERROR: Failed to find il2cpp_raise_managed_exception - failed to find a call to ArgumentException..ctor");
                }
            }
            
            Console.WriteLine("\t\tUsing known functions to resolve others...");

            if (ret.il2cpp_codegen_object_new != 0)
            {
                Console.WriteLine("\t\t\tUsing il2cpp_codegen_object_new to find il2cpp::vm::Object::New...");
                
                Console.WriteLine($"\t\t\t\tDisassembling bytes at 0x{ret.il2cpp_codegen_object_new:X} and checking the first instruction is a JMP...");

                instructions = Utils.GetMethodBodyAtRawAddress(cppAssembly, cppAssembly.MapVirtualAddressToRaw(ret.il2cpp_codegen_object_new), true);

                var shouldBeJMPToVmObjectNew = instructions[0];

                if (shouldBeJMPToVmObjectNew.Mnemonic == ud_mnemonic_code.UD_Ijmp)
                {
                    ret.il2cpp_vm_object_new = Utils.GetJumpTarget(shouldBeJMPToVmObjectNew, shouldBeJMPToVmObjectNew.PC + ret.il2cpp_codegen_object_new);
                    Console.WriteLine($"\t\t\t\tSucceeded! Got address of vm::Object::New function: 0x{ret.il2cpp_vm_object_new:x}");
                }
                else
                    Console.WriteLine($"\t\t\t\tFailed! Expecting a JMP instruction, but it was a {shouldBeJMPToVmObjectNew.Mnemonic}. Direct vm::Object::New calls will not be accounted for!");
            }

            return ret;
        }

        private static void TryUseArrayEnumerator(List<(TypeDefinition type, List<CppMethodData> methods)> methodData, KeyFunctionAddresses ret)
        {
            //ArrayEnumerator.get_Current can get us:
            //    -il2cpp_codegen_initialize_method
            //    -il2cpp_codegen_object_new
            //    -il2cpp_raise_managed_exception
            Console.WriteLine("\t\t\tLooking for Type System.Array/ArrayEnumerator, Method get_Current...");
            var (type, arrayEnumeratorMethods) = methodData.Find(t => t.type.FullName == "System.Array/ArrayEnumerator");
            if (type != null)
            {
                Console.WriteLine("\t\t\t\tType Located. Ensuring method exists...");
                var targetMethod = arrayEnumeratorMethods.Find(m => m.MethodName == "get_Current");
                if (targetMethod.MethodName != null) //Check struct contains valid data 
                {
                    Console.WriteLine("\t\t\t\tTarget Method Located. Ensuring signature matches what we're expecting...");
                    var disasm = Utils.DisassembleBytes(targetMethod.MethodBytes);

                    var callInstructions = disasm.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Icall).ToList();

                    var callTargets = callInstructions.Select(i => Utils.GetJumpTarget(i, targetMethod.MethodOffsetRam + i.PC)).ToList();

                    //First call is codegen_init_method
                    //Second is, or should be, a call to Object$GetType
                    //The next call is a virtual function call so it essentially boils down to (object->klass->vtable[functionId]).methodPtr(obj, object->klass->vtable[functionId].method)
                    //    NB this itself boils down to (deference object to get klass) + 0x128 (vtable) + 0x10 (size of vtable element type) * functionId
                    //Then we have a call to a property getter, specifically Type$get_IsPointer
                    //Then we get a call to il2cpp_codegen_object_new
                    //Then to the ctor of InvalidOperationException
                    //Then to the raise_managed_exception function
                    
                    var potentialCIM = callTargets[0];
                    var shouldPointToObjectGetType = callTargets[1];
                    var shouldPointToTypeGetIsPointer = callTargets[3];
                    var potentialCON = callTargets[4];
                    var shouldPointToIOECtor = callTargets[5];
                    var potentialRME = callTargets[6];

                    //Firstly, sanity checks on the three known addresses we have
                    SharedState.MethodsByAddress.TryGetValue(shouldPointToObjectGetType, out var shouldBeO_GT);
                    SharedState.MethodsByAddress.TryGetValue(shouldPointToTypeGetIsPointer, out var shouldBeT_GIP);
                    SharedState.MethodsByAddress.TryGetValue(shouldPointToIOECtor, out var shouldBeIOE_C);
                    
//                     var shouldBeO_GT = SharedState.MethodsByAddress[shouldPointToObjectGetType];
//                     var shouldBeT_GIP = SharedState.MethodsByAddress[shouldPointToTypeGetIsPointer];
//                     var shouldBeIOE_C = SharedState.MethodsByAddress[shouldPointToIOECtor];

                    if (shouldBeO_GT?.Name == "GetType" && shouldBeT_GIP?.Name == "get_IsPointer" && shouldBeIOE_C?.Name == ".ctor")
                    {
                        Console.WriteLine("\t\t\t\tChecks passed, pulling pointers...");

                        ret.il2cpp_codegen_initialize_method = potentialCIM;
                        ret.il2cpp_codegen_object_new = potentialCON;
                        ret.il2cpp_raise_managed_exception = potentialRME;

                        Console.WriteLine("\t\t\t\tObtained 3 pointers, to il2cpp_codegen_initialize_method, il2cpp_codegen_object_new, and il2cpp_raise_managed_exception");
                    }
                    else
                        Console.WriteLine($"\t\t\t\tMethod does not match expected signature (got {shouldBeO_GT?.FullName} / {shouldBeT_GIP?.FullName} / {shouldBeIOE_C?.FullName}), not using.");
                }
                else
                    Console.WriteLine("\t\t\t\tMethod not present, not using.");
            }
            else
                Console.WriteLine("\t\t\t\tType not present, not using.");
        }
    }
}
