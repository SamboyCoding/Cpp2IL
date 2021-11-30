using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

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
            if (node.FlowControl is InstructionGraphNodeFlowControl.ConditionalJump)
            {
                FixNode(node);
            }
        }
    }

    private void FixNode(X86ControlFlowGraphNode node)
    {
        var jump = node.Instructions.Last();
                    
        var destination = FindNodeByAddress(jump.NearBranch64);

        int index = destination.Instructions.FindIndex(instruction => instruction.IP == jump.NearBranch64);

        var nodeCreated = SplitAndCreate(destination, index);
                    
        if (nodeCreated != null)
        {
            AddNode(nodeCreated);
            AddDirectedEdge(destination, nodeCreated);
            destination = nodeCreated;
        }
        AddDirectedEdge(node, destination);
    }

    private static HashSet<Register> _volatileRegisters = new()
    {
       Register.RCX,
       Register.RDX,
       Register.R8,
       Register.R9,
       Register.R10,
       Register.R11,
       Register.XMM0,
       Register.XMM1,
       Register.XMM2,
       Register.XMM3,
       Register.XMM4,
       Register.XMM5,
    };

    protected override void DetermineLocals()
    {
        TraverseNode(Root);
    }

    private Dictionary<Register, bool> _registersUsed = new ();

    private Dictionary<Instruction, bool> ShouldCreateLocal = new();
    private void TraverseNode(X86ControlFlowGraphNode node)
    {
        
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
            case FlowControl.IndirectBranch:
                return InstructionGraphNodeFlowControl.IndirectJump;
            default:
                throw new NotImplementedException($"Flow control {flowControl} not supported");
                
        }
    }

    protected override void BuildInitialGraph()
    {
        List<X86ControlFlowGraphNode> jmpNodesToCorrect = new List<X86ControlFlowGraphNode>();
        var currentNode = new X86ControlFlowGraphNode() {ID = idCounter++};
        AddNode(currentNode);
        AddDirectedEdge(Root, currentNode);
        for (int i = 0; i < Instructions.Count; i++)
        {
            switch (Instructions[i].FlowControl)
            {
                case FlowControl.UnconditionalBranch:
                    currentNode.AddInstruction(Instructions[i]);
                    var newNodeFromJmp = new X86ControlFlowGraphNode() {ID = idCounter++};
                    AddNode(newNodeFromJmp);
                    var result = Instructions.Any(instruction => instruction.IP == Instructions[i].NearBranch64);
                    if (!result)
                        AddDirectedEdge(currentNode,
                            newNodeFromJmp); // This is a jmp outside of this method, presumably a noreturn method or a tail call probably
                    else
                        jmpNodesToCorrect.Add(newNodeFromJmp);
                    break;
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
                case FlowControl.IndirectBranch:
                    // This could be a part of either 2 things, a jmp to a jump table (switch statement) or a tail call to another function maybe? I dunno
                    throw new NotImplementedException("Indirect branch not implemented currently");
                default:
                    throw new NotImplementedException(Instructions[i].ToString() + " " + Instructions[i].FlowControl);
            }
        }
        for (int i = 0; i < jmpNodesToCorrect.Count; i++)
        {
            FixNode(jmpNodesToCorrect[i]);
        }
    }
}