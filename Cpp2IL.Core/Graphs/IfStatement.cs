using System;
using System.Collections.Generic;
using System.Text;

namespace Cpp2IL.Core.Graphs;

public class IfStatement<TInstruction> : IStatement
{
    private InstructionGraphNode<TInstruction> IfBlock;
    private InstructionGraphNode<TInstruction>? ElseBlock;
    private InstructionGraphCondition<TInstruction> Condition;
    public IfStatement(InstructionGraphCondition<TInstruction> condition, InstructionGraphNode<TInstruction> @if, InstructionGraphNode<TInstruction>? @else)
    {
        Condition = condition;
        IfBlock = @if;
        ElseBlock = @else;
    }

    public IfStatement(InstructionGraphCondition<TInstruction> condition, InstructionGraphNode<TInstruction> @if) : this(condition, @if, null) {}
    
    public string GetTextDump(int indent)
    {
        throw new NotImplementedException();
    }
}