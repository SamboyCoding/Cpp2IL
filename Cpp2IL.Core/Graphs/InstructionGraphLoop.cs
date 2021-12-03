using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public class InstructionGraphLoop<TNode>
{
    public TNode Header;
    public List<TNode> Nodes;

    public InstructionGraphLoop(TNode header)
    {
        Header = header;
        Nodes = new List<TNode>();
        Nodes.Add(header);
    }
}