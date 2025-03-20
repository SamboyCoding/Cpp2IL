using Cpp2IL.Decompiler.ControlFlow;
using Cpp2IL.Decompiler.IL;

namespace Cpp2IL.Decompiler.Transforms;

/// <summary>
/// Builds SSA (static single assignment) form for a method.
/// </summary>
public class BuildSsaForm : ITransform
{
    private Dictionary<int, Stack<Register>> _versions = new();
    private Dictionary<int, int> _versionCount = new();
    private Dictionary<Block, Dictionary<int, Register>> _blockOutVersions = new();

    public void Apply(Method method, IContext context)
    {
        _versions.Clear();
        _versionCount.Clear();
        _blockOutVersions.Clear();

        var graph = method.ControlFlowGraph;
        var dominance = method.Dominance;

        ProcessBlock(graph.EntryBlock, dominance.DominanceTree);
        InsertAllPhiFunctions(graph, dominance, method.Parameters);
    }

    private void InsertAllPhiFunctions(ControlFlowGraph graph, Dominance dominance, List<object> parameters)
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
            for (var i = 0; i < block.Def.Count; i++)
            {
                var operand = block.Def[i];

                if (operand is Register register)
                {
                    if (!defSites.ContainsKey(register.Number))
                        defSites[register.Number] = [];
                    defSites[register.Number].Add(block);
                }
            }
        }

        return defSites;
    }

    private void InsertPhiFunction(List<Register> sources, Block block)
    {
        // Create phi dest, src1, src2, etc.
        var destination = GetNewVersion(sources[0]);
        var phi = new Instruction(-1, OpCode.Phi, destination);

        foreach (var source in sources.Distinct())
            phi.Operands.Add(source);

        // Add it
        block.Instructions.Insert(0, phi);
        // Replace uses
        ReplaceRegistersUntilReassignment(block, 1, destination);
    }

    private static void ReplaceRegistersUntilReassignment(Block block, int startIndex, Register register)
    {
        for (var i = startIndex; i < block.Instructions.Count; i++)
        {
            var instruction = block.Instructions[i];

            // Reassignment?
            if (instruction.Destination is Register destination)
            {
                if (destination.Number == register.Number)
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

                if (operand is MemoryAddress memory)
                {
                    if (memory.Base != null)
                    {
                        var baseRegister = (Register)memory.Base;

                        if (baseRegister.Number == register.Number)
                            memory.Base = register;
                    }

                    if (memory.Index != null)
                    {
                        var index = (Register)memory.Index;

                        if (index.Number == register.Number)
                            memory.Index = register;
                    }

                    instruction.Operands[j] = memory;
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
            // Replace registers with SSA versions
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                if (instruction.Operands[i] is Register register)
                {
                    if (_versions.TryGetValue(register.Number, out var versions))
                        instruction.Operands[i] = register.Copy(versions.Peek().Version);
                }

                if (instruction.Operands[i] is MemoryAddress memory)
                {
                    if (memory.Base != null)
                    {
                        var baseRegister = (Register)memory.Base;

                        if (_versions.TryGetValue(baseRegister.Number, out var versions))
                            memory.Base = baseRegister.Copy(versions.Peek().Version);
                    }

                    if (memory.Index != null)
                    {
                        var indexRegister = (Register)memory.Index;

                        if (_versions.TryGetValue(indexRegister.Number, out var versions))
                            memory.Index = indexRegister.Copy(versions.Peek().Version);
                    }

                    instruction.Operands[i] = memory;
                }
            }

            // Create new version
            if (instruction.Destination is Register destination)
                instruction.Destination = GetNewVersion(destination);
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
        foreach (var instruction in block.Instructions.Where(i => i.Destination is Register))
        {
            var register = (Register)instruction.Destination!;
            _versions.FirstOrDefault(kv => kv.Key == register.Number).Value.Pop();
        }
    }
}
