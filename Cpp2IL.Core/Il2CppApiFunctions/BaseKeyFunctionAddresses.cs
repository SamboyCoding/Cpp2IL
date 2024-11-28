using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Il2CppApiFunctions;

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

    public ulong il2cpp_vm_object_is_inst; //Not exported, not thunked. Can be located via the Type#IsInstanceOfType icall.

    public ulong AddrPInvokeLookup; //TODO Re-find this and fix name

    public IEnumerable<KeyValuePair<string, ulong>> Pairs => resolvedAddressMap;

    private ApplicationAnalysisContext _appContext = null!; //Always initialized before used

    private readonly Dictionary<string, ulong> resolvedAddressMap = [];
    private readonly HashSet<ulong> resolvedAddressSet = [];

    public bool IsKeyFunctionAddress(ulong address)
    {
        return address != 0 && resolvedAddressSet.Contains(address);
    }

    private void FindExport(string name, out ulong ptr)
    {
        Logger.Verbose($"\tLooking for Exported {name} function...");
        ptr = _appContext.Binary.GetVirtualAddressOfExportedFunctionByName(name);

        Logger.VerboseNewline(ptr == 0 ? "Not found" : $"Found at 0x{ptr:X}");
    }

    public virtual void Find(ApplicationAnalysisContext applicationAnalysisContext)
    {
        _appContext = applicationAnalysisContext;
        Init(applicationAnalysisContext);

        //Try to find System.Exception (should always be there)
        if (applicationAnalysisContext.Binary.InstructionSetId == DefaultInstructionSets.X86_32 || applicationAnalysisContext.Binary.InstructionSetId == DefaultInstructionSets.X86_64)
            //TODO make this abstract and implement in subclasses.
            TryGetInitMetadataFromException();

        //New Object
        FindExport("il2cpp_object_new", out il2cpp_object_new);

        //Type => Object
        FindExport("il2cpp_type_get_object", out il2cpp_type_get_object);

        //Resolve ICall
        FindExport("il2cpp_resolve_icall", out il2cpp_resolve_icall);

        //New String
        FindExport("il2cpp_string_new", out il2cpp_string_new);

        //New string wrapper
        FindExport("il2cpp_string_new_wrapper", out il2cpp_string_new_wrapper);

        //Box Value
        FindExport("il2cpp_value_box", out il2cpp_value_box);

        //Unbox Value
        FindExport("il2cpp_object_unbox", out il2cpp_object_unbox);

        //Raise Exception
        FindExport("il2cpp_raise_exception", out il2cpp_raise_exception);

        //Class Init
        FindExport("il2cpp_runtime_class_init", out il2cpp_runtime_class_init_export);

        //New array of fixed size
        FindExport("il2cpp_array_new_specific", out il2cpp_array_new_specific);

        //Object IsInst
        il2cpp_vm_object_is_inst = GetObjectIsInstFromSystemType();

        AttemptInstructionAnalysisToFillGaps();

        FindThunks();
        InitializeResolvedAddresses();
    }

    protected void TryGetInitMetadataFromException()
    {
        //Exception.get_Message() - first call is either to codegen_initialize_method (< v27) or codegen_initialize_runtime_metadata
        Logger.VerboseNewline("\tLooking for Type System.Exception, Method get_Message...");

        var type = LibCpp2IlReflection.GetType("Exception", "System")!;
        Logger.VerboseNewline("\t\tType Located. Ensuring method exists...");
        var targetMethod = type.Methods!.FirstOrDefault(m => m.Name == "get_Message");
        if (targetMethod != null) //Check struct contains valid data 
        {
            Logger.VerboseNewline($"\t\tTarget Method Located at {targetMethod.MethodPointer}. Taking first CALL as the (version-specific) metadata initialization function...");

            var disasm = X86Utils.GetMethodBodyAtVirtAddressNew(targetMethod.MethodPointer, false);
            var calls = disasm.Where(i => i.Mnemonic == Mnemonic.Call).ToList();

            if (calls.Count == 0)
            {
                Logger.WarnNewline("Couldn't find any call instructions in the method body. This is not expected. Will not have metadata initialization function.");
                return;
            }

            if (_appContext.MetadataVersion < 27)
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

    protected abstract ulong GetObjectIsInstFromSystemType();

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

    protected virtual void Init(ApplicationAnalysisContext context)
    {
    }

    private void InitializeResolvedAddresses()
    {
        resolvedAddressMap.Clear();
        resolvedAddressSet.Clear();

        AddResolved(il2cpp_codegen_initialize_method);
        AddResolved(il2cpp_codegen_initialize_runtime_metadata);
        AddResolved(il2cpp_vm_metadatacache_initializemethodmetadata);
        AddResolved(il2cpp_runtime_class_init_export);
        AddResolved(il2cpp_runtime_class_init_actual);
        AddResolved(il2cpp_object_new);
        AddResolved(il2cpp_vm_object_new);
        AddResolved(il2cpp_codegen_object_new);
        AddResolved(il2cpp_array_new_specific);
        AddResolved(il2cpp_vm_array_new_specific);
        AddResolved(SzArrayNew);
        AddResolved(il2cpp_type_get_object);
        AddResolved(il2cpp_vm_reflection_get_type_object);
        AddResolved(il2cpp_resolve_icall);
        AddResolved(InternalCalls_Resolve);

        AddResolved(il2cpp_string_new);
        AddResolved(il2cpp_vm_string_new);
        AddResolved(il2cpp_string_new_wrapper);
        AddResolved(il2cpp_vm_string_newWrapper);
        AddResolved(il2cpp_codegen_string_new_wrapper);

        AddResolved(il2cpp_value_box);
        AddResolved(il2cpp_vm_object_box);

        AddResolved(il2cpp_object_unbox);
        AddResolved(il2cpp_vm_object_unbox);

        AddResolved(il2cpp_raise_exception);
        AddResolved(il2cpp_vm_exception_raise);
        AddResolved(il2cpp_codegen_raise_exception);

        AddResolved(il2cpp_vm_object_is_inst);

        AddResolved(AddrPInvokeLookup);

        void AddResolved(ulong address, [CallerArgumentExpression(nameof(address))] string name = "")
        {
            resolvedAddressSet.Add(address);
            resolvedAddressMap[name] = address;
        }
    }
}
