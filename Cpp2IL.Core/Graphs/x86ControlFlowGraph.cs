using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;

namespace Cpp2IL.Core;

public class X86ControlFlowGraph : AbstractControlFlowGraph<Instruction, X86ControlFlowGraphNode>
{
    public X86ControlFlowGraph(List<Instruction> instructions) : base(instructions)
    {
    }

    protected override ulong GetAddressOfInstruction(Instruction instruction) => instruction.IP;

    protected override void SegmentGraph()
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.FlowControl != null && node.FlowControl == InstructionGraphNodeFlowControl.ConditionalJump)
            {
                var conditionalBranchInstruction = node.Instructions.Last();
                    
                var destination = FindNodeByAddress(conditionalBranchInstruction.NearBranch64);

                int index = destination.Instructions.FindIndex(instruction => instruction.IP == conditionalBranchInstruction.NearBranch64);

                var nodeCreated = SplitAndCreate(destination, index, idCounter++);
                    
                if (nodeCreated != null)
                {
                    AddNode(nodeCreated);
                    AddDirectedEdge(destination, nodeCreated);
                    i++;
                    destination = nodeCreated;
                }
                AddDirectedEdge(node, destination);
                    
            }
        }
    }

    protected override void ExtractFeatures()
    {
        // TODO:
    }

    private InstructionGraphNodeFlowControl GetAbstractControlFlow(FlowControl flowControl)
    {
        switch (flowControl)
        {
            case FlowControl.Call:
                return InstructionGraphNodeFlowControl.Call;
            case FlowControl.UnconditionalBranch:
                return InstructionGraphNodeFlowControl.UnconditionalJump;
            case FlowControl.IndirectCall:
                return InstructionGraphNodeFlowControl.IndirectCall;
            case FlowControl.Return:
                return InstructionGraphNodeFlowControl.Return;
            case FlowControl.Next:
                return InstructionGraphNodeFlowControl.Continue;
            case FlowControl.Interrupt:
                return InstructionGraphNodeFlowControl.NoReturn;
            case FlowControl.ConditionalBranch:
                return InstructionGraphNodeFlowControl.ConditionalJump;
            default:
                throw new NotImplementedException($"Flow control {flowControl} not supported");
                
        }
    }

    protected override void BuildInitialGraph()
    {
        var currentNode = new X86ControlFlowGraphNode() {ID = idCounter++};
            AddNode(currentNode);
            AddDirectedEdge(Root, currentNode);
            for (int i = 0; i < Instructions.Count; i++)
            {
                switch (Instructions[i].FlowControl)
                {
                    case FlowControl.UnconditionalBranch:
                    case FlowControl.IndirectCall:
                    case FlowControl.Call:
                        currentNode.AddInstruction(Instructions[i]);
                        var newNodeFromCall = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromCall);
                        AddDirectedEdge(currentNode, newNodeFromCall);
                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromCall;
                        break;
                    case FlowControl.Next:
                        currentNode.AddInstruction(Instructions[i]);
                        break;
                    case FlowControl.Return:
                        currentNode.AddInstruction(Instructions[i]);
                        var newNodeFromReturn = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromReturn);
                        AddDirectedEdge(currentNode, EndNode);
                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromReturn;
                        break;
                    case FlowControl.ConditionalBranch:
                        currentNode.AddInstruction(Instructions[i]);
                        var newNodeFromConditionalBranch = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromConditionalBranch);
                        AddDirectedEdge(currentNode, newNodeFromConditionalBranch);
                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromConditionalBranch;
                        break;
                    case FlowControl.Interrupt:
                        currentNode.AddInstruction(Instructions[i]);
                        var newNodeFromInterrupt = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromInterrupt);
                        AddDirectedEdge(currentNode, EndNode);
                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromInterrupt;
                        break;
                    default:
                        throw new NotImplementedException(Instructions[i].ToString() + " " + Instructions[i].FlowControl);
                }
            }
    }
}