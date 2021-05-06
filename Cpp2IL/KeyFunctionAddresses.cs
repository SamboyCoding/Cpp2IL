using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.PE;
using SharpDisasm.Udis86;
using Instruction = SharpDisasm.Instruction;

namespace Cpp2IL
{
    public class KeyFunctionAddresses
    {
        public ulong il2cpp_vm_object_new;
        public ulong il2cpp_codegen_object_new;

        public ulong il2cpp_vm_metadatacache_initializemethodmetadata;
        public ulong il2cpp_codegen_initialize_method;
        public ulong il2cpp_codegen_initialize_runtime_metadata;

        public ulong il2cpp_runtime_class_init_export;
        public ulong il2cpp_runtime_class_init_actual;

        public ulong il2cpp_array_new_specific;
        public ulong il2cpp_vm_array_new_specific;
        public ulong SzArrayNew;

        public ulong il2cpp_type_get_object;

        public ulong il2cpp_resolve_icall; //Api function (exported)
        public ulong InternalCalls_Resolve; //Thunked from above.

        public ulong il2cpp_value_box;
        public ulong il2cpp_object_box;
        public ulong il2cpp_object_is_inst;
        public ulong il2cpp_raise_managed_exception;
        public ulong AddrPInvokeLookup;

        public static KeyFunctionAddresses Find()
        {
            var cppAssembly = LibCpp2IlMain.Binary!;
            var ret = new KeyFunctionAddresses();

            //First: The function that sets up a method, only needs to be located so we can ignore it.
            //Many easy places to get this, but ideally we need a CLR method without any overloads so it's not ambiguous

            //Chosen Target: ArgumentCache#TidyAssemblyTypeName
            //That's in UnityEngine.CoreModule.dll but close enough
            //I don't even know what that method does but whatever

            Console.WriteLine("\t\tSearching for methods known to contain certain calls...");

            //Try to find System.Exception (should always be there)
            TryGetInitMetadataFromException(ret);

            //Try to find System.Array/ArrayEnumerator
            TryUseArrayEnumerator(ret);

            //Try use exported il2cpp_object_new
            TryUseExportedIl2CppObjectNew(ret);

            Console.Write("\t\t\tLooking for Exported il2cpp_type_get_object function...");
            ret.il2cpp_type_get_object = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_type_get_object");
            Console.WriteLine($"Found at 0x{ret.il2cpp_type_get_object:X}");

            Console.Write("\t\t\tLooking for Exported il2cpp_resolve_icall function...");
            ret.il2cpp_resolve_icall = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_resolve_icall");
            Console.WriteLine($"Found at 0x{ret.il2cpp_resolve_icall:X}");

            if (ret.il2cpp_resolve_icall != 0)
            {
                Console.Write("\t\t\tMapping il2cpp_resolve_icall to InternalCalls::Resolve...");
                ret.InternalCalls_Resolve = FindFunctionThisIsAThunkOf(ret.il2cpp_resolve_icall);
                Console.WriteLine($"Found at 0x{ret.InternalCalls_Resolve:X}");
            }
            
            Console.Write("\t\t\tGrabbing il2cpp_runtime_class_init from exports...");
            ret.il2cpp_runtime_class_init_export = ((PE) cppAssembly).GetVirtualAddressOfPeExportByName("il2cpp_runtime_class_init");
            Console.WriteLine($"Got address 0x{ret.il2cpp_runtime_class_init_export:X}");

            Console.Write("\t\t\tDisassembling to get il2cpp:vm::Runtime::ClassInit...");
            ret.il2cpp_runtime_class_init_actual = FindFunctionThisIsAThunkOf(ret.il2cpp_runtime_class_init_export);
            Console.WriteLine($"Got address 0x{ret.il2cpp_runtime_class_init_actual:X}");

            //Try and find il2cpp_array_new_specific
            TryUseExportedIl2CppArrayNewSpecific(ret);

            //TODO: move everything else to this ^ format so we can pick-and-choose which structs we need 

            Console.WriteLine("\t\t\tLooking for UnityEngine.Events.ArgumentCache$TidyAssemblyTypeName...");

            List<Instruction> instructions;
            ulong addr;

            Console.WriteLine("\t\t\tLooking for System.RuntimeType$GetGenericArgumentsInternal...");
            var methods = Utils.TryLookupTypeDefKnownNotGeneric("System.RuntimeType")?.Methods;

            if (methods != null)
            {
                var method = methods.FirstOrDefault(m => m.Name == "GetGenericArgumentsInternal" && m.AsUnmanaged().parameterCount == 0);

                Console.WriteLine($"\t\t\t\tSearching for a call to the safe cast function near offset 0x{method.AsUnmanaged()!.MethodPointer:X}...");

                instructions = Utils.DisassembleBytes(LibCpp2IlMain.Binary.is32Bit, method.AsUnmanaged().CppMethodBodyBytes.ToArray());

                var calls = instructions.Where(insn => insn.Mnemonic == ud_mnemonic_code.UD_Icall).ToArray();

                addr = Utils.GetJumpTarget(calls[2], method.AsUnmanaged()!.MethodPointer + calls[2].PC);
                Console.WriteLine($"\t\t\t\tLocated il2cpp_object_is_inst function at 0x{addr:X}");
                ret.il2cpp_object_is_inst = addr;
            }

            if (ret.il2cpp_raise_managed_exception == 0)
            {
                Console.WriteLine("\t\t\tUsing EXPORT method to find il2cpp_raise_managed_exception...");
                TryUseExportedIl2cppRaiseException(ret);
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

        private static void TryGetInitMetadataFromException(KeyFunctionAddresses ret)
        {
            //Exception.get_Message() - first call is either to codegen_initialize_method (< v27) or codegen_initialize_runtime_metadata
            Console.WriteLine("\t\t\tLooking for Type System.Exception, Method get_Message...");

            var type = Utils.TryLookupTypeDefKnownNotGeneric("System.Exception");
            if (type != null)
            {
                Console.WriteLine("\t\t\t\tType Located. Ensuring method exists...");
                var targetMethod = type.Methods.FirstOrDefault(m => m.Name == "get_Message");
                if (targetMethod != null) //Check struct contains valid data 
                {
                    Console.WriteLine("\t\t\t\tTarget Method Located. Taking first CALL as the (version-specific) metadata initialization function...");

                    var disasm = LibCpp2ILUtils.DisassembleBytesNew(LibCpp2IlMain.Binary!.is32Bit, targetMethod.AsUnmanaged().CppMethodBodyBytes, targetMethod.AsUnmanaged().MethodPointer);
                    var calls = disasm.Where(i => i.Mnemonic == Mnemonic.Call).ToList();

                    if (LibCpp2IlMain.MetadataVersion < 27)
                    {
                        ret.il2cpp_codegen_initialize_method = calls.First().NearBranchTarget;
                        Console.WriteLine($"\t\t\t\til2cpp_codegen_initialize_method => 0x{ret.il2cpp_codegen_initialize_method:X}");
                    }
                    else
                    {
                        ret.il2cpp_codegen_initialize_runtime_metadata = calls.First().NearBranchTarget;
                        Console.WriteLine($"\t\t\t\til2cpp_codegen_initialize_runtime_metadata => 0x{ret.il2cpp_codegen_initialize_runtime_metadata:X}");
                    }
                }
            }
        }

        private static void TryUseArrayEnumerator(KeyFunctionAddresses ret)
        {
            //ArrayEnumerator.get_Current can get us:
            //    -il2cpp_codegen_initialize_method
            //    -il2cpp_codegen_object_new
            //    -il2cpp_raise_managed_exception
            Console.WriteLine("\t\t\tLooking for Type System.Array/ArrayEnumerator, Method get_Current...");
            var type = Utils.TryLookupTypeDefKnownNotGeneric("System.Array/ArrayEnumerator");
            if (type != null)
            {
                Console.WriteLine("\t\t\t\tType Located. Ensuring method exists...");
                var targetMethod = type.Methods.FirstOrDefault(m => m.Name == "get_Current");
                if (targetMethod != null)
                {
                    Console.WriteLine("\t\t\t\tTarget Method Located. Ensuring signature matches what we're expecting...");
                    var disasm = Utils.DisassembleBytes(LibCpp2IlMain.Binary.is32Bit, targetMethod.AsUnmanaged().CppMethodBodyBytes.ToArray());

                    var callInstructions = disasm.Where(i => i.Mnemonic == ud_mnemonic_code.UD_Icall).ToList();

                    var callTargets = callInstructions.Select(i => Utils.GetJumpTarget(i, targetMethod.AsUnmanaged().MethodPointer + i.PC)).ToList();

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

                        if (LibCpp2IlMain.MetadataVersion < 27)
                            ret.il2cpp_codegen_initialize_method = potentialCIM;
                        else
                            ret.il2cpp_codegen_initialize_runtime_metadata = potentialCIM;

                        ret.il2cpp_codegen_object_new = potentialCON;
                        ret.il2cpp_raise_managed_exception = potentialRME;

                        //Sometimes this CIM pointer actually points at vm::MetadataCache::InitializeMethodMetadata
                        var cimMethodBody = Utils.GetMethodBodyAtRawAddress(LibCpp2IlMain.Binary, LibCpp2IlMain.Binary.MapVirtualAddressToRaw(ret.il2cpp_codegen_initialize_method), true);
                        if (cimMethodBody[0].Mnemonic == ud_mnemonic_code.UD_Ijmp)
                        {
                            //We have the actual cim, and the jump target is MC::IMM
                            ret.il2cpp_vm_metadatacache_initializemethodmetadata = Utils.GetJumpTarget(cimMethodBody[0], cimMethodBody[0].PC + ret.il2cpp_codegen_initialize_method);
                        }
                        else
                        {
                            //We have MC::IMM
                            ret.il2cpp_vm_metadatacache_initializemethodmetadata = ret.il2cpp_codegen_initialize_method;
                            ret.il2cpp_codegen_initialize_method = 0;
                        }

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

        private static void TryUseExportedIl2CppObjectNew(KeyFunctionAddresses ret)
        {
            Console.Write("\t\t\tLooking for exported il2cpp_object_new function...");
            var address = ((PE) LibCpp2IlMain.Binary).GetVirtualAddressOfPeExportByName("il2cpp_object_new");
            Console.WriteLine($"Found at 0x{address:X}");

            Console.Write("\t\t\t\tSearching for a CALL to vm::Object::New...");
            var instructions = Utils.GetMethodBodyAtVirtAddressNew(LibCpp2IlMain.Binary, address, true);
            try
            {
                var matchingCall = instructions.First(i => i.Mnemonic == Mnemonic.Call);

                ret.il2cpp_vm_object_new = matchingCall.NearBranchTarget;
                Console.WriteLine($"Located address of il2cpp::vm::Object::New: 0x{ret.il2cpp_vm_object_new:X}");

                Console.Write("\t\t\t\tLooking for a solo unconditional jump to Object::New...");

                ret.il2cpp_codegen_object_new = FindThunkFunction(ret.il2cpp_vm_object_new);
                Console.WriteLine($"Found il2cpp_codegen_object_new at 0x{ret.il2cpp_codegen_object_new:X}");
            }
            catch (Exception)
            {
                Console.WriteLine("\t\t\t\tCould not find or disassemble call. Failed to find il2cpp_object_new.");
            }
        }

        private static void TryUseExportedIl2CppArrayNewSpecific(KeyFunctionAddresses ret)
        {
            Console.Write("\t\t\tLooking for exported il2cpp_array_new_specific function...");
            ret.il2cpp_array_new_specific = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_array_new_specific");
            Console.WriteLine($"Found at 0x{ret.il2cpp_array_new_specific:X}");

            Console.Write("\t\t\t\tSearching for a JMP to vm::Array::NewSpecific...");
            var instructions = Utils.GetMethodBodyAtVirtAddressNew(LibCpp2IlMain.Binary, ret.il2cpp_array_new_specific, true);
            try
            {
                var matchingCall = instructions.First(i => i.Mnemonic == Mnemonic.Jmp);

                ret.il2cpp_vm_array_new_specific = matchingCall.NearBranchTarget;
                Console.WriteLine($"Located address of il2cpp::vm::Array::NewSpecific: 0x{ret.il2cpp_vm_object_new:X}");

                Console.Write("\t\t\t\tLooking for a thunk function proxying Array::NewSpecific...");

                ret.SzArrayNew = FindThunkFunction(ret.il2cpp_vm_array_new_specific, 4, ret.il2cpp_array_new_specific);
                Console.WriteLine($"Found SzArrayNew at 0x{ret.SzArrayNew:X}");
            }
            catch (Exception)
            {
                Console.WriteLine("\t\t\t\tCould not find or disassemble call. Failed to find vm::Array::NewSpecific.");
            }
        }

        private static ulong FindThunkFunction(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
        {
            //Disassemble .text
            var allInstructions = ((PE) LibCpp2IlMain.Binary!).DisassembleTextSection();

            //Find all jumps to the target address
            var matchingJmps = allInstructions.Where(i => i.Mnemonic == Mnemonic.Jmp && i.NearBranchTarget == addr).ToList();

            foreach (var matchingJmp in matchingJmps)
            {
                if (addressesToIgnore.Contains(matchingJmp.IP)) continue;

                //Find this instruction in the raw file
                var offsetInPe = (ulong) LibCpp2IlMain.Binary.MapVirtualAddressToRaw((uint) matchingJmp.IP);
                if (offsetInPe == 0 || offsetInPe == (ulong) (LibCpp2IlMain.Binary!.RawLength - 1))
                    continue;

                //get next and previous bytes
                var previousByte = LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe - 1);
                var nextByte = LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe + (ulong) matchingJmp.Length);

                //Double-cc = thunk
                if (previousByte == 0xCC && nextByte == 0xCC)
                {
                    return matchingJmp.IP;
                }

                if (nextByte == 0xCC && maxBytesBack > 0)
                {
                    for (ulong backtrack = 1; backtrack < maxBytesBack && offsetInPe - backtrack > 0; backtrack++)
                    {
                        if (LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe - backtrack) == 0xCC)
                            return matchingJmp.IP - (backtrack - 1);
                    }
                }
            }

            return 0;
        }

        private static void TryUseExportedIl2cppRaiseException(KeyFunctionAddresses ret)
        {
            Console.Write("\t\t\t\tLooking for exported il2cpp_raise_exception function...");
            var address = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_raise_exception");
            Console.WriteLine($"Found at 0x{address:X}");

            Console.WriteLine("\t\t\t\tSearching for a CALL to il2cpp_raise_managed_exception...");
            ret.il2cpp_raise_managed_exception = FindFunctionThisIsAThunkOf(address);
            
            if (ret.il2cpp_raise_managed_exception != 0)
                Console.WriteLine($"\t\t\tLocated address of il2cpp_raise_managed_exception: 0x{ret.il2cpp_raise_managed_exception:X}");
            else
                Console.WriteLine("\t\t\t\tCould not find or disassemble call. Failed to find il2cpp_raise_managed_exception.");
        }

        private static ulong FindFunctionThisIsAThunkOf(ulong thunkPtr)
        {
            var instructions = Utils.GetMethodBodyAtVirtAddressNew(LibCpp2IlMain.Binary!, thunkPtr, true);

            try
            {
                var matchingCall = instructions.First(i => i.Mnemonic == Mnemonic.Jmp);

                return matchingCall.NearBranchTarget;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}