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
    private Dictionary<Block, Dictionary<int, Register>> _blockOutVersions = new();

    public void Apply(Method method)
    {
        _versions.Clear();
        _versionCount.Clear();

        var graph = method.ControlFlowGraph;
        var dominance = method.Dominance;

        ProcessBlock(graph.EntryBlock, dominance.DominanceTree);
        InsertAllPhiFunctions(graph, dominance, method.Parameters);
    }

    private void InsertAllPhiFunctions(ControlFlowGraph graph, Dominance dominance, List<IOperand> parameters)
    {
        // Check where registers are defined
        var defSites = GetDefinitionSites(graph);

        // For each register
        foreach (var entry in defSites)
        {
            var regNumber = entry.Key;

            var workList = new Queue<Block>(entry.Value);
            var phiInserted = new HashSet<Block>();

            while (workList.Count > 0)
            {
                var block = workList.Dequeue();

                // For each dominance frontier block of the current block
                if (!dominance.DominanceFrontier.TryGetValue(block, out var dfBlocks))
                    continue;

                foreach (var dfBlock in dfBlocks)
                {
                    // Already visited
                    if (phiInserted.Contains(dfBlock)) continue;

                    // For each predecessor, get it's last register version
                    var sources = new List<Register>();
                    foreach (var pred in dfBlock.Predecessors)
                    {
                        if (_blockOutVersions.TryGetValue(pred, out var mapping)
                            && mapping.TryGetValue(regNumber, out var versionedReg))
                        {
                            sources.Add(versionedReg);
                        }
                        else
                        {
                            // It's not in predecessors so it's probably a parameter
                            var param = parameters.OfType<Register>().FirstOrDefault(p => p.Number == regNumber);
                            sources.Add(param);
                        }
                    }

                    // Insert phi into the frontier block
                    InsertPhiFunction(sources, dfBlock);
                    phiInserted.Add(dfBlock);

                    // If dfBlock doesn't define this register, add it to queue
                    var defines = dfBlock.Def.Any(operand => operand is Register r && r.Number == regNumber);
                    if (!defines)
                        workList.Enqueue(dfBlock);
                }
            }
        }
    }

    private static Dictionary<int, HashSet<Block>> GetDefinitionSites(ControlFlowGraph graph)
    {
        // Check what registers are defined and where
        var defSites = new Dictionary<int, HashSet<Block>>();
        foreach (var block in graph.Blocks)
        {
            foreach (var operand in block.Def)
            {
                if (operand is not Register reg) continue;

                if (!defSites.ContainsKey(reg.Number))
                    defSites[reg.Number] = [];
                defSites[reg.Number].Add(block);
            }
        }

        return defSites;
    }

    private void InsertPhiFunction(List<Register> sources, Block block)
    {
        // Create phi src1, src2...
        var phi = new Instruction(-1, OpCode.Phi);
        foreach (var source in sources)
            phi.Operands.Add(source);

        // Move its value to something
        var result = GetNewVersion(sources[0]);
        phi = new Instruction(-1, OpCode.Move, result, phi);

        // Add it
        block.Instructions.Insert(0, phi);
        // Replace uses
        ReplaceRegistersUntilReassignment(block, 1, result);
    }

    private static void ReplaceRegistersUntilReassignment(Block block, int startIndex, Register register)
    {
        for (var i = startIndex; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];

            // Reassignment?
            if (instruction.OpCode == OpCode.Move)
            {
                if (((Register)instruction.Operands[0]!).Number == register.Number)
                    return;
            }

            // Replace it
            for (var j = 0; j < instruction.Operands.Count; j++)
            {
                var operand = instruction.Operands[j];

                if (operand is Register register2)
                {
                    if (register2.Number == register.Number)
                        instruction.Operands[j] = register;
                }

                if (operand is CallInfo call)
                {
                    for (var k = 0; k < call.Parameters.Count; k++)
                    {
                        var param = call.Parameters[k];

                        if (param is Register paramRegister)
                        {
                            if (paramRegister.Number == register.Number)
                                call.Parameters[k] = register;
                        }
                    }
                }
            }
        }
    }

    private Register GetNewVersion(Register old)
    {
        if (!_versionCount.ContainsKey(old.Number))
        {
            // Params are version 0
            _versionCount.Add(old.Number, 1);
            _versions.Add(old.Number, new Stack<Register>());
            _versions[old.Number].Push(old.Copy(0));
        }

        _versionCount[old.Number]++;
        var newRegister = old.Copy(_versionCount[old.Number]);
        _versions[old.Number].Push(newRegister);
        return newRegister;
    }

    private void ProcessBlock(Block block, Dictionary<Block, List<Block>> dominanceTree)
    {
        foreach (var instruction in block.Instructions)
        {
            // Create new version
            if (instruction.OpCode == OpCode.Move)
            {
                var destination = (Register)instruction.Operands[0]!;
                var newRegister = GetNewVersion(destination);
                instruction.Operands[0] = newRegister;
            }

            ReplaceRegistersWithSsaVersions(instruction);
        }

        // Record last register version
        var outMapping = new Dictionary<int, Register>();
        foreach (var kvp in _versions)
        {
            if (kvp.Value.Count > 0)
                outMapping[kvp.Key] = kvp.Value.Peek();
        }

        _blockOutVersions[block] = outMapping;

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

            if (instruction.Operands[i] is Register register)
            {
                if (_versions.TryGetValue(register.Number, out var versions))
                    instruction.Operands[i] = register.Copy(versions.Peek().Version);
            }

            if (instruction.Operands[i] is CallInfo call)
            {
                for (var j = 0; j < call.Parameters.Count; j++)
                {
                    var param = call.Parameters[j];

                    if (param is Register paramRegister)
                    {
                        if (_versions.TryGetValue(paramRegister.Number, out var versions))
                            call.Parameters[j] = paramRegister.Copy(versions.Peek().Version);
                    }
                }
            }
        }
    }
}
