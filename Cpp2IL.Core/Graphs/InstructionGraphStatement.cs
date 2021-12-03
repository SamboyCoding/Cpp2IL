using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public class InstructionGraphStatement<TInstruction>
{
    public InstructionGraphStatementType Type { get; }
    public InstructionGraphCondition<TInstruction>? Expression;
    public List<InstructionGraphNode<TInstruction>>? Blocks;
    public InstructionGraphStatement(InstructionGraphStatementType type)
    {
        Type = type;
    }
}