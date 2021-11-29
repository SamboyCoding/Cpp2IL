using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Iced.Intel;

namespace Cpp2IL.Core;

public class InstructionGraphNodeSet<T> : Collection<InstructionGraphNode<T>>
{
    
}
public class InstructionGraphNode<T>
{
    public int ID { get; set; }

    public virtual bool IsCondtionalBranch => _flowControl == InstructionGraphNodeFlowControl.ConditionalJump;

    public Condition<T>? Condition { get; protected set; }
    public InstructionGraphNode<T>? TrueTarget { get; protected set; }
    public InstructionGraphNode<T>? FalseTarget { get; protected set; }

    public InstructionGraphNode()
    {
        Instructions = new();
        _neighbors = new();
    }
        
    private InstructionGraphNodeSet<T> _neighbors;
        
    public InstructionGraphNodeSet<T> Neighbors
    {
        get => _neighbors;
        set => _neighbors = value;
    }

    private InstructionGraphNodeFlowControl? _flowControl;
        
    public InstructionGraphNodeFlowControl? FlowControl
    {
        get => _flowControl;
        set => _flowControl = value;
    }
        
    public void AddInstruction(T instruction) => Instructions.Add(instruction);

    public void CheckCondition()
    {
        if (Neighbors.Count != 2)
        {
            // This sometimes happens very rarely where the second successor is M.I.A despite it being a conditional block, pain
            throw new Exception($"Node didn't have 2 neighbours, instead had {Neighbors.Count}, aborting...\n\nNode Dump:\n{GetTextDump()}");
        }
        if (!ThisNodeHasComparison())
        {
            // TODO: Ideally we would check our predecessors recursively and check if that one has a condition.
            // If theres two possible ways that the condition could be made from,
            // say one inside an if block and one inside an else block and our conditional jump
            // is after those two block we'll need to think of something smart I guess
            throw new Exception($"Comparison wasn't found in this block, aborting...");
        }
        CreateCondition();
    }

    public virtual void CreateCondition()
    {
        throw new NotImplementedException();
    }
        

    protected string GetTextDump()
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append($"ID: {ID}, FlowControl: {_flowControl}");
        if (IsCondtionalBranch)
            stringBuilder.Append($", Condition: {Condition?.ConditionString}");
        stringBuilder.Append('\n');
        foreach (var instruction in Instructions)
        {
            stringBuilder.AppendLine(GetFormattedInstructionAddress(instruction) + " " + instruction.ToString());
        }
        return stringBuilder.ToString();
    }

    public virtual string GetFormattedInstructionAddress(T instruction) => throw new NotImplementedException();
    public virtual bool ThisNodeHasComparison()
    {
        throw new NotImplementedException();
    }

    public List<T> Instructions { get; }
}