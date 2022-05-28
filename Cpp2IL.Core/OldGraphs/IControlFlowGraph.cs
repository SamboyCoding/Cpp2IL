using System;
using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public interface IControlFlowGraph
{
    public void Run(bool print = false);
    
    public List<IControlFlowNode> INodes { get; }

    public void TraverseEntireGraphPreOrder(Action<IControlFlowNode> action);
}