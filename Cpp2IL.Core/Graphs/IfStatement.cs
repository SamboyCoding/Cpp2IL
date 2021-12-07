using System;
using System.Collections.Generic;
using System.Text;

namespace Cpp2IL.Core.Graphs;

public class IfStatement<TInstruction> : IStatement
{
    private List<IStatement> IfBlock;
    private List<IStatement> ElseBlock;
    private InstructionGraphCondition<TInstruction> Condition;
    public IfStatement(InstructionGraphCondition<TInstruction> condition, List<IStatement> @if, List<IStatement> @else)
    {
        Condition = condition;
        IfBlock = @if;
        ElseBlock = @else;
    }

    public IfStatement(InstructionGraphCondition<TInstruction> condition, List<IStatement> @if) : this(condition, @if, new List<IStatement>()) {}
    
    public string GetTextDump(int indent)
    {
        StringBuilder stringBuilder = new StringBuilder();
        var space = new string(' ', indent);
        stringBuilder.Append(space).Append($"if({Condition.ConditionString})\n");
        stringBuilder.Append(space).Append("{\n");
        
        foreach (var statement in IfBlock)
            stringBuilder.Append(statement.GetTextDump(indent + 4));

        stringBuilder.Append(space).Append("}\n");
        if (ElseBlock.Count == 0)
            return stringBuilder.ToString();

        stringBuilder.Append(space).Append("else\n");
        stringBuilder.Append(space).Append("{\n");
        
        foreach (var statement in ElseBlock)
            stringBuilder.Append(statement.GetTextDump(indent + 4));

        stringBuilder.Append(space).Append("}\n");

        return stringBuilder.ToString();
    }
}