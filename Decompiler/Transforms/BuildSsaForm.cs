using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Builds SSA (static single assignment) form for a method.
/// </summary>
public class BuildSsaForm : ITransform
{
    private Dictionary<int, Stack<Register>> _versions = new();
    private Dictionary<int, int> _versionCount = new();

    public void Apply(Method method)
    {
        _versions.Clear();
        _versionCount.Clear();

        var graph = method.ControlFlowGraph;
        var dominance = method.Dominance;

        ProcessBlock(graph.EntryBlock, dominance.DominanceTree);
        // TODO: Insert phi functions
    }

    private static void ReplaceRegistersUntilReassignment(Block block, int startIndex, Register register)
    {
        for (var i = startIndex; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];

            // Reassignment?
            if (instruction.OpCode == OpCode.Move)
            {
                if ((Register)instruction.Operands[0]! == register)
                    return;
            }

            // Replace it
            for (var j = 0; j < instruction.Operands.Count; j++)
            {
                var operand = instruction.Operands[j];

                if (operand is Register register2)
                {
                    if (register2 == register)
                        instruction.Operands[j] = register;
                }
            }
        }
    }

    private void GetNewVersion(Register old, out Register newRegister)
    {
        if (!_versionCount.ContainsKey(old.Number))
        {
            // Params are version 0
            _versionCount.Add(old.Number, 1);
            _versions.Add(old.Number, new Stack<Register>());
            _versions[old.Number].Push(old.Copy(0));
        }

        _versionCount[old.Number]++;
        newRegister = old.Copy(_versionCount[old.Number]);
        _versions[old.Number].Push(newRegister);
    }

    private void ProcessBlock(Block block, Dictionary<Block, List<Block>> dominanceTree)
    {
        foreach (var instruction in block.Instructions)
        {
            // Create new version
            if (instruction.OpCode == OpCode.Move)
            {
                var destination = (Register)instruction.Operands[0]!;
                GetNewVersion(destination, out var newRegister);
                instruction.Operands[0] = newRegister;
            }

            ReplaceRegistersWithSsaVersions(instruction);
        }

        // Process children in the tree
        if (dominanceTree.TryGetValue(block, out var children))
        {
            foreach (var child in children)
                ProcessBlock(child, dominanceTree);
        }

        // Remove registers from versions but not from count
        foreach (var instruction in block.Instructions.Where(instr => instr.OpCode == OpCode.Move))
        {
            var register = (Register)instruction.Operands[0]!;
            _versions.FirstOrDefault(kv => kv.Key == register.Number).Value.Pop();
        }
    }

    private void ReplaceRegistersWithSsaVersions(Instruction instruction)
    {
        for (var i = 0; i < instruction.Operands.Count; i++)
        {
            if (instruction.Operands[i] is Instruction instructionOp)
            {
                ReplaceRegistersWithSsaVersions(instructionOp);
                continue;
            }

            if (instruction.Operands[i] is not Register register) continue;

            if (_versions.TryGetValue(register.Number, out var versions))
                instruction.Operands[i] = register.Copy(versions.Peek().Version);
        }
    }
}
