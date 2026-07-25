using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Maps calls to KeyFunctionAddresses to their underlying IL opcodes. E.g. il2cpp_codegen_object_new => newobj.
/// Eventually will include box/unbox/throw/etc
/// </summary>
public static class KeyFunctionRecovery
{
    //All of these have the same params in the same order so we treat them as equal.
    private static readonly HashSet<string> ObjectNewFunctions =
    [
        "il2cpp_object_new",
        "il2cpp_vm_object_new",
        "il2cpp_codegen_object_new",
    ];

    public static void Run(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Operands is not [string keyFunction, ..])
                continue;

            if (ObjectNewFunctions.Contains(keyFunction))
                RewriteObjectNew(instruction);
            else if (keyFunction == nameof(BaseKeyFunctionAddresses.il2cpp_codegen_write_barrier))
                RemoveWriteBarrier(instruction);
        }
    }

    private static void RemoveWriteBarrier(Instruction instruction)
    {
        instruction.OpCode = OpCode.Nop;
        instruction.Operands = [];
    }
    
    private static void RewriteObjectNew(Instruction instruction)
    {
        // Needs the function name, the result, and the class argument.
        if (instruction.OpCode != OpCode.Call || instruction.Operands.Count < 3)
            return;

        var result = instruction.Operands[1];
        var klass = instruction.Operands[2];

        instruction.OpCode = OpCode.Newobj;
        instruction.Operands = [result, klass];
    }
}
