using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Graphs;

public class InstructionGraphNode<TInstruction> : IControlFlowNode
{
    public int ID { get; set; }

    public bool IsConditionalBranch => _flowControl == InstructionGraphNodeFlowControl.ConditionalJump;

    public InstructionGraphCondition<TInstruction>? Condition { get; protected internal set; }

    public InstructionGraphNode()
    {
        Instructions = new();
        Successors = new();
        Predecessors = new();
        Statements = new();
    }

    public InstructionGraphNodeSet<TInstruction> Successors { get; set; }
    public InstructionGraphNodeSet<TInstruction> Predecessors { get; set; }
    
    public List<IStatement> Statements { get;}

    private InstructionGraphNodeFlowControl? _flowControl;

    public bool HasProcessedSuccessors = false;

    public bool NeedsCorrectingDueToJump = false;

    public bool Visited = false;

    public BitArray? Dominators;
        
    public InstructionGraphNodeFlowControl? FlowControl
    {
        get => _flowControl;
        set => _flowControl = value;
    }
        
    public void AddInstruction(TInstruction instruction) => Instructions.Add(instruction);

    public void CheckCondition()
    {
        if (Successors.Count != 2)
        {
            throw new Exception($"Node didn't have 2 neighbours, instead had {Successors.Count}, aborting...\n\nNode Dump:\n{GetTextDump()}");
        }

        var node = this;
        while(!node.ThisNodeHasComparison())
            node = node.Predecessors.Count == 1 ? node.Predecessors.Single() : throw new NodeConditionCalculationException("Don't have a comparison and don't have a single predecessor line to a node which has one");

        var lastComparison = node.GetLastComparison();
        
        CreateCondition(lastComparison);

        if (Condition is not null)
        {
            Instructions.Remove(Condition.Jump);
            node.Instructions.Remove(lastComparison);
        }
    }

    protected virtual void CreateCondition(TInstruction comparison)
    {
        throw new NotImplementedException();
    }

    protected virtual TInstruction GetLastComparison() => throw new NotImplementedException();

    protected string GetTextDump()
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append($"ID: {ID}, FlowControl: {_flowControl}");
        if (IsConditionalBranch)
            stringBuilder.Append($", Condition: {Condition?.ConditionString}");
        stringBuilder.Append('\n');
        foreach (var instruction in Instructions)
        {
            stringBuilder.AppendLine(GetFormattedInstructionAddress(instruction) + " " + instruction);
        }
        return stringBuilder.ToString();
    }

    public virtual string GetFormattedInstructionAddress(TInstruction instruction) => throw new NotImplementedException();
    public virtual bool ThisNodeHasComparison()
    {
        throw new NotImplementedException();
    }

    public List<TInstruction> Instructions { get; } = new();
    public List<InstructionSetIndependentInstruction> TranslatedInstructions = new();
}