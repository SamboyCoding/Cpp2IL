using System.Linq;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Graphs.Processors
{
    internal class CallProcessor : IBlockProcessor
    {
        public void Process(Block block, ApplicationAnalysisContext appContext)
        {
            if (block.BlockType != BlockType.Call)
                return;
            var callInstruction = block.isilInstructions.Last();
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

            var keyFunctionAddresses = appContext.GetOrCreateKeyFunctionAddresses();

            if (keyFunctionAddresses.IsKeyFunctionAddress(target))
            {
                HandleKeyFunction(appContext, callInstruction, target, keyFunctionAddresses);
                return;
            }
            else
            {
                // TODO: We could possibly try to resolve some non de-duplicated managed methods early on?
            }
        }

        private void HandleKeyFunction(ApplicationAnalysisContext appContext, InstructionSetIndependentInstruction instruction, ulong target, BaseKeyFunctionAddresses kFA)
        {
            return;
            // TODO: Handle labelling functions calls that match these in a more graceful manner
            var method = "";
            if (target == kFA.il2cpp_codegen_initialize_method || target == kFA.il2cpp_codegen_initialize_runtime_metadata)
            {
                if (appContext.MetadataVersion < 27)
                {
                    method = "il2cpp_codegen_initialize_method";
                }
                else
                {
                    method = "il2cpp_codegen_initialize_runtime_metadata";
                }
            }
            else if (target == kFA.il2cpp_vm_metadatacache_initializemethodmetadata)
            {
                method = "il2cpp_vm_metadatacache_initializemethodmetadata";
            }
            else if (target == kFA.il2cpp_runtime_class_init_export)
            {
                method = "il2cpp_runtime_class_init_export";
            }
            else if (target == kFA.il2cpp_runtime_class_init_actual)
            {
                method = "il2cpp_runtime_class_init_actual";
            }
            else if (target == kFA.il2cpp_object_new)
            {
                method = "il2cpp_vm_object_new";
            }
            else if (target == kFA.il2cpp_codegen_object_new)
            {
                method = "il2cpp_codegen_object_new";
            }
            else if (target == kFA.il2cpp_array_new_specific)
            {
                method = "il2cpp_array_new_specific";
            }
            else if (target == kFA.il2cpp_vm_array_new_specific)
            {
                method = "il2cpp_vm_array_new_specific";
            }
            else if (target == kFA.SzArrayNew)
            {
                method = "SzArrayNew";
            }
            else if (target == kFA.il2cpp_type_get_object)
            {
                method = "il2cpp_type_get_object";
            }
            else if (target == kFA.il2cpp_vm_reflection_get_type_object)
            {
                method = "il2cpp_vm_reflection_get_type_object";
            }
            else if (target == kFA.il2cpp_resolve_icall)
            {
                method = "il2cpp_resolve_icall";
            }
            else if (target == kFA.InternalCalls_Resolve)
            {
                method = "InternalCalls_Resolve";
            }
            else if (target == kFA.il2cpp_string_new)
            {
                method = "il2cpp_string_new";
            }
            else if (target == kFA.il2cpp_vm_string_new)
            {
                method = "il2cpp_vm_string_new";
            }
            else if (target == kFA.il2cpp_string_new_wrapper)
            {
                method = "il2cpp_string_new_wrapper";
            }
            else if (target == kFA.il2cpp_vm_string_newWrapper)
            {
                method = "il2cpp_vm_string_newWrapper";
            }
            else if (target == kFA.il2cpp_codegen_string_new_wrapper)
            {
                method = "il2cpp_codegen_string_new_wrapper";
            }
            else if (target == kFA.il2cpp_value_box)
            {
                method = "il2cpp_value_box";
            }
            else if (target == kFA.il2cpp_vm_object_box)
            {
                method = "il2cpp_vm_object_box";
            }
            else if (target == kFA.il2cpp_object_unbox)
            {
                method = "il2cpp_object_unbox";
            }
            else if (target == kFA.il2cpp_vm_object_unbox)
            {
                method = "il2cpp_vm_object_unbox";
            }
            else if (target == kFA.il2cpp_raise_exception)
            {
                method = "il2cpp_raise_exception";
            }
            else if (target == kFA.il2cpp_vm_exception_raise)
            {
                method = "il2cpp_vm_exception_raise";
            }
            else if (target == kFA.il2cpp_codegen_raise_exception)
            {
                method = "il2cpp_codegen_raise_exception";
            }
            else if (target == kFA.il2cpp_vm_object_is_inst)
            {
                method = "il2cpp_vm_object_is_inst";
            }
            else if (target == kFA.AddrPInvokeLookup)
            {
                method = "AddrPInvokeLookup";
            }
            if (method != "")
            {
                instruction.Operands[0] = InstructionSetIndependentOperand.MakeImmediate(method);
            }
        }
    }
}
