using System;
using System.Collections.Generic;
using System.Text;

namespace Cpp2IL.Core.Graphs;

public class IfStatement<TInstruction> : IStatement
{
    private List<IStatement> IfBlock;
    private List<IStatement>? ElseBlock;
    private InstructionGraphCondition<TInstruction> Condition;
    public IfStatement(InstructionGraphCondition<TInstruction> condition, List<IStatement> @if, List<IStatement>? @else)
    {
        Condition = condition;
        IfBlock = @if;
        ElseBlock = @else;
    }

    public IfStatement(InstructionGraphCondition<TInstruction> condition, List<IStatement> @if) : this(condition, @if, new List<IStatement>()) {}
    
    public string GetTextDump(int indent)
    {
        throw new NotImplementedException();
    }
}