using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

/// <summary>
/// Ordered case targets
/// </summary>
/// <param name="blocks"></param>
public class SwitchTargets(List<Graphs.Block> blocks) : IOperand
{
    public List<Graphs.Block> Blocks { get; } = blocks;
}
