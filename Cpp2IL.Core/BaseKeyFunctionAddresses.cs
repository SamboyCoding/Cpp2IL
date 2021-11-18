﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Core
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public abstract class BaseKeyFunctionAddresses
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
        public ulong il2cpp_string_new_wrapper; //Api function
        public ulong il2cpp_vm_string_newWrapper; //Thunked from above
        public ulong il2cpp_codegen_string_new_wrapper; //Not sure if actual name, used in ARM64 attribute gens, thunks TO above.
        
        public ulong il2cpp_value_box; //Api function (exported)
        public ulong il2cpp_vm_object_box; //Thunked from above

        public ulong il2cpp_object_unbox; //Api function
        public ulong il2cpp_vm_object_unbox; //Thunked from above
        
        public ulong il2cpp_raise_exception; //Api function (exported)
        public ulong il2cpp_vm_exception_raise; //Thunked from above
        public ulong il2cpp_codegen_raise_exception; //Thunked TO above. don't know real name.
        public ulong il2cpp_object_is_inst; //TODO Re-find this and fix name
        public ulong AddrPInvokeLookup; //TODO Re-find this and fix name

        private void FindExport(string name, ref ulong ptr)
        {
            Logger.Verbose($"\tLooking for Exported {name} function...");
            ptr = LibCpp2IlMain.Binary!.GetVirtualAddressOfExportedFunctionByName(name);
            
            if(ptr == 0)
                Logger.VerboseNewline("Not found");
            else
                Logger.VerboseNewline($"Found at 0x{ptr:X}");
        }

        public void Find()
        {
            var cppAssembly = LibCpp2IlMain.Binary!;

            //Try to find System.Exception (should always be there)
            if(cppAssembly.InstructionSet is InstructionSet.X86_32 or InstructionSet.X86_64)
                //TODO make this abstract and implement in subclasses.
                TryGetInitMetadataFromException();

            //New Object
            FindExport("il2cpp_object_new", ref il2cpp_object_new);

            //Type => Object
            FindExport("il2cpp_type_get_object", ref il2cpp_type_get_object);

            //Resolve ICall
            FindExport("il2cpp_resolve_icall", ref il2cpp_resolve_icall);

            //New String
            FindExport("il2cpp_string_new", ref il2cpp_string_new);

            //New string wrapper
            FindExport("il2cpp_string_new_wrapper", ref il2cpp_string_new_wrapper);

            //Box Value
            FindExport("il2cpp_value_box", ref il2cpp_value_box);

            //Unbox Value
            FindExport("il2cpp_object_unbox", ref il2cpp_object_unbox);

            //Raise Exception
            FindExport("il2cpp_raise_exception", ref il2cpp_raise_exception);

            //Class Init
            FindExport("il2cpp_runtime_class_init", ref il2cpp_runtime_class_init_export);

            //New array of fixed size
            FindExport("il2cpp_array_new_specific", ref il2cpp_array_new_specific);
            
            AttemptInstructionAnalysisToFillGaps();
            
            FindThunks();
        }

        protected void TryGetInitMetadataFromException()
        {
            //Exception.get_Message() - first call is either to codegen_initialize_method (< v27) or codegen_initialize_runtime_metadata
            Logger.VerboseNewline("\tLooking for Type System.Exception, Method get_Message...");

            var type = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Exception");
            if (type != null)
            {
                Logger.VerboseNewline("\t\tType Located. Ensuring method exists...");
                var targetMethod = type.Methods.FirstOrDefault(m => m.Name == "get_Message");
                if (targetMethod != null) //Check struct contains valid data 
                {
                    Logger.VerboseNewline($"\t\tTarget Method Located at {targetMethod.AsUnmanaged().MethodPointer}. Taking first CALL as the (version-specific) metadata initialization function...");
                    
                    var disasm = X86Utils.GetMethodBodyAtVirtAddressNew(targetMethod.AsUnmanaged().MethodPointer, false);
                    var calls = disasm.Where(i => i.Mnemonic == Mnemonic.Call).ToList();

                    if (LibCpp2IlMain.MetadataVersion < 27)
                    {
                        il2cpp_codegen_initialize_method = calls.First().NearBranchTarget;
                        Logger.VerboseNewline($"\t\til2cpp_codegen_initialize_method => 0x{il2cpp_codegen_initialize_method:X}");
                    }
                    else
                    {
                        il2cpp_codegen_initialize_runtime_metadata = calls.First().NearBranchTarget;
                        Logger.VerboseNewline($"\t\til2cpp_codegen_initialize_runtime_metadata => 0x{il2cpp_codegen_initialize_runtime_metadata:X}");
                    }
                }
            }
        }

        protected virtual void AttemptInstructionAnalysisToFillGaps()
        {
            
        }

        private void FindThunks()
        {
            if (il2cpp_object_new != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_object_new to vm::Object::New...");
                il2cpp_vm_object_new = FindFunctionThisIsAThunkOf(il2cpp_object_new, true);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_object_new:X}");
            }

            if (il2cpp_vm_object_new != 0)
            {
                Logger.Verbose("\t\tLooking for il2cpp_codegen_object_new as a thunk of vm::Object::New...");
                
                var potentialThunks = FindAllThunkFunctions(il2cpp_vm_object_new, 16);
                
                //Sort by caller count in ascending order
                var list = potentialThunks.Select(ptr => (ptr, count: GetCallerCount(ptr))).ToList();
                list.SortByExtractedKey(pair => pair.count);

                //Sort in descending order - most called first
                list.Reverse();
                
                //Take first as the target
                il2cpp_codegen_object_new = list.FirstOrDefault().ptr;
                
                Logger.VerboseNewline($"Found at 0x{il2cpp_codegen_object_new:X}");
            }
            
            if (il2cpp_type_get_object != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_resolve_icall to Reflection::GetTypeObject...");
                il2cpp_vm_reflection_get_type_object = FindFunctionThisIsAThunkOf(il2cpp_type_get_object);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_reflection_get_type_object:X}");
            }
            
            if (il2cpp_resolve_icall != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_resolve_icall to InternalCalls::Resolve...");
                InternalCalls_Resolve = FindFunctionThisIsAThunkOf(il2cpp_resolve_icall);
                Logger.VerboseNewline($"Found at 0x{InternalCalls_Resolve:X}");
            }
            
            if (il2cpp_string_new != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_string_new to String::New...");
                il2cpp_vm_string_new = FindFunctionThisIsAThunkOf(il2cpp_string_new);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_string_new:X}");
            }
            
            if (il2cpp_string_new_wrapper != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_string_new_wrapper to String::NewWrapper...");
                il2cpp_vm_string_newWrapper = FindFunctionThisIsAThunkOf(il2cpp_string_new_wrapper);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_string_newWrapper:X}");
            }

            if (il2cpp_vm_string_newWrapper != 0)
            {
                Logger.Verbose("\t\tMapping String::NewWrapper to il2cpp_codegen_string_new_wrapper...");
                il2cpp_codegen_string_new_wrapper = FindAllThunkFunctions(il2cpp_vm_string_newWrapper, 0, il2cpp_string_new_wrapper).FirstOrDefault();
                Logger.VerboseNewline($"Found at 0x{il2cpp_codegen_string_new_wrapper:X}");
            }

            if (il2cpp_value_box != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_value_box to Object::Box...");
                il2cpp_vm_object_box = FindFunctionThisIsAThunkOf(il2cpp_value_box);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_object_box:X}");
            }
            
            if (il2cpp_object_unbox != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_object_unbox to Object::Unbox...");
                il2cpp_vm_object_unbox = FindFunctionThisIsAThunkOf(il2cpp_object_unbox);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_object_unbox:X}");
            }
            
            if (il2cpp_raise_exception != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_raise_exception to il2cpp::vm::Exception::Raise...");
                il2cpp_vm_exception_raise = FindFunctionThisIsAThunkOf(il2cpp_raise_exception, true);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_exception_raise:X}");
            }

            if (il2cpp_vm_exception_raise != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp::vm::Exception::Raise to il2cpp_codegen_raise_exception...");
                il2cpp_codegen_raise_exception = FindAllThunkFunctions(il2cpp_vm_exception_raise, 4, il2cpp_raise_exception).FirstOrDefault();
                Logger.VerboseNewline($"Found at 0x{il2cpp_codegen_raise_exception:X}");
            }
            
            if (il2cpp_runtime_class_init_export != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_runtime_class_init to il2cpp:vm::Runtime::ClassInit...");
                il2cpp_runtime_class_init_actual = FindFunctionThisIsAThunkOf(il2cpp_runtime_class_init_export);
                Logger.VerboseNewline($"Found at 0x{il2cpp_runtime_class_init_actual:X}");
            }

            if (il2cpp_array_new_specific != 0)
            {
                Logger.Verbose("\t\tMapping il2cpp_array_new_specific to vm::Array::NewSpecific...");
                il2cpp_vm_array_new_specific = FindFunctionThisIsAThunkOf(il2cpp_array_new_specific);
                Logger.VerboseNewline($"Found at 0x{il2cpp_vm_array_new_specific:X}");
            }

            if (il2cpp_vm_array_new_specific != 0)
            {
                Logger.Verbose("\t\tLooking for SzArrayNew as a thunk function proxying Array::NewSpecific...");
                SzArrayNew = FindAllThunkFunctions(il2cpp_vm_array_new_specific, 4, il2cpp_array_new_specific).FirstOrDefault();
                Logger.VerboseNewline($"Found at 0x{SzArrayNew:X}");
            }
        }

        /// <summary>
        /// Given a function at addr, find a function which serves no purpose other than to call addr.
        /// </summary>
        /// <param name="addr">The address of the function to call.</param>
        /// <param name="maxBytesBack">The maximum number of bytes to go back from any branching instructions to find the actual start of the thunk function.</param>
        /// <param name="addressesToIgnore">A list of function addresses which this function must not return</param>
        /// <returns>The address of the first function in the file which thunks addr, starts within maxBytesBack bytes of the branch, and is not contained within addressesToIgnore, else 0 if none can be found.</returns>
        protected abstract IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore);

        /// <summary>
        /// Given a function at thunkPtr, return the address of the function that said function exists only to call.
        /// That is, given a function which performs no meaningful operations other than to call x, return the address of x.
        /// </summary>
        /// <param name="thunkPtr">The address of the thunk function</param>
        /// <param name="prioritiseCall">True to prioritise "call" statements - conditional flow transfer - over "jump" statements - unconditional flow transfer. False for the inverse.</param>
        /// <returns>The address of the thunked function, if it can be found, else 0</returns>
        protected abstract ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false);

        protected abstract int GetCallerCount(ulong toWhere);
    }
}