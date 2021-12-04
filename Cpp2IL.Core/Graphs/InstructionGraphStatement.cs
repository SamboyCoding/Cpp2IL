using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public class InstructionGraphStatement<TInstruction>
{
    public InstructionGraphStatementType Type { get; }
    public InstructionGraphCondition<TInstruction>? Expression;
    public List<InstructionGraphNode<TInstruction>>? Nodes;
    public InstructionGraphNode<TInstruction>? ContinueNode;
    public InstructionGraphNode<TInstruction>? BreakNode;
    public InstructionGraphStatement(InstructionGraphStatementType type)
    {
        Type = type;
    }
}