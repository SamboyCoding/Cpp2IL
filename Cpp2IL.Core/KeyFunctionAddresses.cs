using System;
using System.Linq;
using Cpp2IL.Core.Exceptions;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.PE;

namespace Cpp2IL.Core
{
    public class KeyFunctionAddresses
    {
        public ulong il2cpp_codegen_initialize_method; //Either this
        public ulong il2cpp_codegen_initialize_runtime_metadata; //Or this, are present, depending on metadata version, but not exported.
        public ulong il2cpp_vm_metadatacache_initializemethodmetadata; //This is thunked from the above (but only pre-27?)

        public ulong il2cpp_runtime_class_init_export; //Api function (exported)
        public ulong il2cpp_runtime_class_init_actual; //Thunked from above

        public ulong il2cpp_object_new; //Api Function (exported)
        public ulong il2cpp_vm_object_new; //Thunked from above
        public ulong il2cpp_codegen_object_new; //Thunked TO above
        
        public ulong il2cpp_array_new_specific; //Api function (exported)
        public ulong il2cpp_vm_array_new_specific; //Thunked from above
        public ulong SzArrayNew; //Thunked TO above.

        public ulong il2cpp_type_get_object; //Api function (exported)
        public ulong il2cpp_vm_reflection_get_type_object; //Thunked from above

        public ulong il2cpp_resolve_icall; //Api function (exported)
        public ulong InternalCalls_Resolve; //Thunked from above.

        public ulong il2cpp_string_new; //Api function (exported)
        public ulong il2cpp_vm_string_new; //Thunked from above

        public ulong il2cpp_value_box; //Api function (exported)
        public ulong il2cpp_vm_object_box; //Thunked from above

        public ulong il2cpp_raise_exception; //Api function (exported)
        public ulong il2cpp_raise_managed_exception; //Thunked from above
        
        public ulong il2cpp_object_is_inst; //TODO Re-find this and fix name
        
        public ulong AddrPInvokeLookup; //TODO Re-find this and fix name

        public static KeyFunctionAddresses Find()
        {
            if (LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_32 && LibCpp2IlMain.Binary?.InstructionSet != InstructionSet.X86_64)
                throw new UnsupportedInstructionSetException();

            var cppAssembly = LibCpp2IlMain.Binary!;
            var ret = new KeyFunctionAddresses();

            //Try to find System.Exception (should always be there)
            TryGetInitMetadataFromException(ret);

            //New Object
            Logger.Verbose("\tLooking for Exported il2cpp_object_new function...");
            ret.il2cpp_object_new = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_object_new");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_object_new:X}");

            if (ret.il2cpp_object_new != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_object_new to vm::Object::New...");
                ret.il2cpp_vm_object_new = FindFunctionThisIsAThunkOf(ret.il2cpp_object_new, true);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_vm_object_new:X}");
            }

            if (ret.il2cpp_vm_object_new != 0)
            {
                Logger.Verbose("\t\tLooking for il2cpp_codegen_object_new as a thunk of vm::Object::New...");
                ret.il2cpp_codegen_object_new = FindThunkFunction(ret.il2cpp_vm_object_new);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_codegen_object_new:X}");
            }

            //Type => Object
            Logger.Verbose("\tLooking for Exported il2cpp_type_get_object function...");
            ret.il2cpp_type_get_object = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_type_get_object");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_type_get_object:X}");
            
            if (ret.il2cpp_type_get_object != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_resolve_icall to Reflection::GetTypeObject...");
                ret.il2cpp_vm_reflection_get_type_object = FindFunctionThisIsAThunkOf(ret.il2cpp_type_get_object);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_vm_reflection_get_type_object:X}");
            }

            //Resolve ICall
            Logger.Verbose("\tLooking for Exported il2cpp_resolve_icall function...");
            ret.il2cpp_resolve_icall = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_resolve_icall");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_resolve_icall:X}");
            
            if (ret.il2cpp_resolve_icall != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_resolve_icall to InternalCalls::Resolve...");
                ret.InternalCalls_Resolve = FindFunctionThisIsAThunkOf(ret.il2cpp_resolve_icall);
                Logger.VerboseNewline($"Found at 0x{ret.InternalCalls_Resolve:X}");
            }
            
            //New String
            Logger.Verbose("\tLooking for Exported il2cpp_string_new function...");
            ret.il2cpp_string_new = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_string_new");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_string_new:X}");
            
            if (ret.il2cpp_string_new != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_string_new to String::New...");
                ret.il2cpp_vm_string_new = FindFunctionThisIsAThunkOf(ret.il2cpp_string_new);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_vm_string_new:X}");
            }
            
            //Box Value
            Logger.Verbose("\tLooking for Exported il2cpp_value_box function...");
            ret.il2cpp_value_box = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_value_box");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_string_new:X}");
            
            if (ret.il2cpp_value_box != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_value_box to Object::Box...");
                ret.il2cpp_vm_object_box = FindFunctionThisIsAThunkOf(ret.il2cpp_value_box);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_vm_object_box:X}");
            }
            
            //Raise Exception
            Logger.Verbose("\tLooking for exported il2cpp_raise_exception function...");
            ret.il2cpp_raise_exception = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_raise_exception");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_raise_exception:X}");

            if (ret.il2cpp_raise_exception != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_raise_exception to il2cpp_raise_managed_exception...");
                ret.il2cpp_raise_managed_exception = FindFunctionThisIsAThunkOf(ret.il2cpp_raise_exception, true);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_raise_managed_exception:X}");
            }

            //Class Init
            Logger.Verbose("\tLooking for exported il2cpp_runtime_class_init function...");
            ret.il2cpp_runtime_class_init_export = ((PE) cppAssembly).GetVirtualAddressOfPeExportByName("il2cpp_runtime_class_init");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_runtime_class_init_export:X}");

            if (ret.il2cpp_runtime_class_init_export != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_runtime_class_init to il2cpp:vm::Runtime::ClassInit...");
                ret.il2cpp_runtime_class_init_actual = FindFunctionThisIsAThunkOf(ret.il2cpp_runtime_class_init_export);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_runtime_class_init_actual:X}");
            }
            
            //New array of fixed size
            Logger.Verbose("\tLooking for exported il2cpp_array_new_specific function...");
            ret.il2cpp_array_new_specific = ((PE) LibCpp2IlMain.Binary!).GetVirtualAddressOfPeExportByName("il2cpp_array_new_specific");
            Logger.VerboseNewline($"Found at 0x{ret.il2cpp_array_new_specific:X}");

            if (ret.il2cpp_array_new_specific != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_array_new_specific to vm::Array::NewSpecific...");
                ret.il2cpp_vm_array_new_specific = FindFunctionThisIsAThunkOf(ret.il2cpp_array_new_specific);
                Logger.VerboseNewline($"Found at 0x{ret.il2cpp_vm_array_new_specific:X}");
            }

            if (ret.il2cpp_vm_array_new_specific != 0)
            {
                Logger.Verbose("\t\tLooking for SzArrayNew as a thunk function proxying Array::NewSpecific...");
                ret.SzArrayNew = FindThunkFunction(ret.il2cpp_vm_array_new_specific, 4, ret.il2cpp_array_new_specific);
                Logger.VerboseNewline($"Found at 0x{ret.SzArrayNew:X}");
            }

            return ret;
        }

        private static void TryGetInitMetadataFromException(KeyFunctionAddresses ret)
        {
            //Exception.get_Message() - first call is either to codegen_initialize_method (< v27) or codegen_initialize_runtime_metadata
            Logger.VerboseNewline("\tLooking for Type System.Exception, Method get_Message...");

            var type = Utils.TryLookupTypeDefKnownNotGeneric("System.Exception");
            if (type != null)
            {
                Logger.VerboseNewline("\t\tType Located. Ensuring method exists...");
                var targetMethod = type.Methods.FirstOrDefault(m => m.Name == "get_Message");
                if (targetMethod != null) //Check struct contains valid data 
                {
                    Logger.VerboseNewline("\t\tTarget Method Located. Taking first CALL as the (version-specific) metadata initialization function...");

                    var disasm = LibCpp2ILUtils.DisassembleBytesNew(LibCpp2IlMain.Binary!.is32Bit, targetMethod.AsUnmanaged().CppMethodBodyBytes, targetMethod.AsUnmanaged().MethodPointer);
                    var calls = disasm.Where(i => i.Mnemonic == Mnemonic.Call).ToList();

                    if (LibCpp2IlMain.MetadataVersion < 27)
                    {
                        ret.il2cpp_codegen_initialize_method = calls.First().NearBranchTarget;
                        Logger.VerboseNewline($"\t\til2cpp_codegen_initialize_method => 0x{ret.il2cpp_codegen_initialize_method:X}");
                    }
                    else
                    {
                        ret.il2cpp_codegen_initialize_runtime_metadata = calls.First().NearBranchTarget;
                        Logger.VerboseNewline($"\t\til2cpp_codegen_initialize_runtime_metadata => 0x{ret.il2cpp_codegen_initialize_runtime_metadata:X}");
                    }
                }
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
                var offsetInPe = (ulong) LibCpp2IlMain.Binary.MapVirtualAddressToRaw(matchingJmp.IP);
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
                        if(addressesToIgnore.Contains(matchingJmp.IP - (backtrack - 1)))
                            //Move to next jmp
                            break;
                        
                        if (LibCpp2IlMain.Binary!.GetByteAtRawAddress(offsetInPe - backtrack) == 0xCC)
                            return matchingJmp.IP - (backtrack - 1);
                    }
                }
            }

            return 0;
        }

        private static ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool useCall = false)
        {
            var instructions = Utils.GetMethodBodyAtVirtAddressNew(thunkPtr, true);

            try
            {
                var target = useCall ? Mnemonic.Call : Mnemonic.Jmp;
                var matchingCall = instructions.First(i => i.Mnemonic == target);

                return matchingCall.NearBranchTarget;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}