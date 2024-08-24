using System.Linq;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Graphs.Processors;

internal class CallProcessor : IBlockProcessor
{
    public void Process(MethodAnalysisContext methodAnalysisContext, Block block)
    {
        if (block.BlockType != BlockType.Call)
            return;
        var callInstruction = block.isilInstructions[^1];
        if (callInstruction == null)
            return;
        if (callInstruction.OpCode != InstructionSetIndependentOpCode.Call)
            return;

        if (callInstruction.Operands.Length <= 0)
            return;
        var dest = callInstruction.Operands[0];
        if (dest.Type != InstructionSetIndependentOperand.OperandType.Immediate)
            return;

        var target = (ulong)((IsilImmediateOperand)dest.Data).Value;

        var keyFunctionAddresses = methodAnalysisContext.AppContext.GetOrCreateKeyFunctionAddresses();

        if (keyFunctionAddresses.IsKeyFunctionAddress(target))
        {
            HandleKeyFunction(methodAnalysisContext.AppContext, callInstruction, target, keyFunctionAddresses);
            return;
        }

        //Non-key function call. Try to find a single match
        if (!methodAnalysisContext.AppContext.MethodsByAddress.TryGetValue(target, out var targetMethods))
            return;

        if (targetMethods is not [{ } singleTargetMethod])
            return;

        callInstruction.Operands[0] = InstructionSetIndependentOperand.MakeMethodReference(singleTargetMethod);
    }

    private void HandleKeyFunction(ApplicationAnalysisContext appContext, InstructionSetIndependentInstruction instruction, ulong target, BaseKeyFunctionAddresses kFA)
    {
        // TODO: Handle labelling functions calls that match these in a more graceful manner
        var method = "";
        if (target == kFA.il2cpp_codegen_initialize_method || target == kFA.il2cpp_codegen_initialize_runtime_metadata)
        {
            if (appContext.MetadataVersion < 27)
            {
                method = nameof(kFA.il2cpp_codegen_initialize_method);
            }
            else
            {
                method = nameof(kFA.il2cpp_codegen_initialize_runtime_metadata);
            }
        }
        else if (target == kFA.il2cpp_vm_metadatacache_initializemethodmetadata)
        {
            method = nameof(kFA.il2cpp_vm_metadatacache_initializemethodmetadata);
        }
        else if (target == kFA.il2cpp_runtime_class_init_export)
        {
            method = nameof(kFA.il2cpp_runtime_class_init_export);
        }
        else if (target == kFA.il2cpp_runtime_class_init_actual)
        {
            method = nameof(kFA.il2cpp_runtime_class_init_actual);
        }
        else if (target == kFA.il2cpp_object_new)
        {
            method = nameof(kFA.il2cpp_vm_object_new);
        }
        else if (target == kFA.il2cpp_codegen_object_new)
        {
            method = nameof(kFA.il2cpp_codegen_object_new);
        }
        else if (target == kFA.il2cpp_array_new_specific)
        {
            method = nameof(kFA.il2cpp_array_new_specific);
        }
        else if (target == kFA.il2cpp_vm_array_new_specific)
        {
            method = nameof(kFA.il2cpp_vm_array_new_specific);
        }
        else if (target == kFA.SzArrayNew)
        {
            method = nameof(kFA.SzArrayNew);
        }
        else if (target == kFA.il2cpp_type_get_object)
        {
            method = nameof(kFA.il2cpp_type_get_object);
        }
        else if (target == kFA.il2cpp_vm_reflection_get_type_object)
        {
            method = nameof(kFA.il2cpp_vm_reflection_get_type_object);
        }
        else if (target == kFA.il2cpp_resolve_icall)
        {
            method = nameof(kFA.il2cpp_resolve_icall);
        }
        else if (target == kFA.InternalCalls_Resolve)
        {
            method = nameof(kFA.InternalCalls_Resolve);
        }
        else if (target == kFA.il2cpp_string_new)
        {
            method = nameof(kFA.il2cpp_string_new);
        }
        else if (target == kFA.il2cpp_vm_string_new)
        {
            method = nameof(kFA.il2cpp_vm_string_new);
        }
        else if (target == kFA.il2cpp_string_new_wrapper)
        {
            method = nameof(kFA.il2cpp_string_new_wrapper);
        }
        else if (target == kFA.il2cpp_vm_string_newWrapper)
        {
            method = nameof(kFA.il2cpp_vm_string_newWrapper);
        }
        else if (target == kFA.il2cpp_codegen_string_new_wrapper)
        {
            method = nameof(kFA.il2cpp_codegen_string_new_wrapper);
        }
        else if (target == kFA.il2cpp_value_box)
        {
            method = nameof(kFA.il2cpp_value_box);
        }
        else if (target == kFA.il2cpp_vm_object_box)
        {
            method = nameof(kFA.il2cpp_vm_object_box);
        }
        else if (target == kFA.il2cpp_object_unbox)
        {
            method = nameof(kFA.il2cpp_object_unbox);
        }
        else if (target == kFA.il2cpp_vm_object_unbox)
        {
            method = nameof(kFA.il2cpp_vm_object_unbox);
        }
        else if (target == kFA.il2cpp_raise_exception)
        {
            method = nameof(kFA.il2cpp_raise_exception);
        }
        else if (target == kFA.il2cpp_vm_exception_raise)
        {
            method = nameof(kFA.il2cpp_vm_exception_raise);
        }
        else if (target == kFA.il2cpp_codegen_raise_exception)
        {
            method = nameof(kFA.il2cpp_codegen_raise_exception);
        }
        else if (target == kFA.il2cpp_vm_object_is_inst)
        {
            method = nameof(kFA.il2cpp_vm_object_is_inst);
        }
        else if (target == kFA.AddrPInvokeLookup)
        {
            method = nameof(kFA.AddrPInvokeLookup);
        }

        if (method != "")
        {
            instruction.Operands[0] = InstructionSetIndependentOperand.MakeImmediate(method);
        }
    }
}
