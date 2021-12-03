using System;
using System.Collections.Generic;
using System.Linq;
using Gee.External.Capstone;
using Iced.Intel;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Graphs;

public class X86ControlFlowGraph : AbstractControlFlowGraph<Instruction, X86ControlFlowGraphNode>
{
    public bool Is32Bit;
    public X86ControlFlowGraph(List<Instruction> instructions, bool is32Bit = false) : base(instructions)
    {
        Is32Bit = is32Bit;
    }

    protected override ulong GetAddressOfInstruction(Instruction instruction) => instruction.IP;

    protected override void SegmentGraph()
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            
            if(node.HasProcessedSuccessors)
                continue;
            
            if (node.FlowControl is InstructionGraphNodeFlowControl.ConditionalJump)
                FixNode(node);

            node.HasProcessedSuccessors = true;
        }
    }

    private void FixNode(X86ControlFlowGraphNode node)
    {
        if(node.FlowControl is InstructionGraphNodeFlowControl.Continue)
            return; //Can happen if we split this node during BuildInitialGraph jumpNodesToCorrect processing.

        var jump = node.Instructions.Last();

        var destination = FindNodeByAddress(jump.NearBranchTarget);

        if (destination == null)
        {
            //We assume that we're tail calling another method somewhere. Need to verify if this breaks anywhere but it shouldn't in general
            node.FlowControl = InstructionGraphNodeFlowControl.Call;
            return;
            // throw new($"While fixing conditional jump node {node.ID}, couldn't find destination node at 0x{jump.NearBranchTarget:X}, near branch from 0x{jump.IP:X}");
        }

        int index = destination.Instructions.FindIndex(instruction => instruction.IP == jump.NearBranchTarget);

        var targetNode = SplitAndCreate(destination, index);
        
        AddDirectedEdge(node, targetNode);
        node.NeedsCorrectingDueToJump = false;
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
    private static InstructionInfoFactory _instructionInfoFactory = new();
    private void TraverseNode(InstructionGraphNode<Instruction> node)
    {
        node.Visited = true;
        uint stackOffset = 0;
        
        // Pre visit
        
        foreach (var succ in node.Successors)
        {
            if (!succ.Visited)
                TraverseNode(succ);
        }
        
        // Post visit
        
        for (int i = 0; i < node.Instructions.Count; i++)
        {
            var nodeInstruction = node.Instructions[i];
            var info = _instructionInfoFactory.GetInfo(nodeInstruction);
            // Crude stack calculation. 
            // if (nodeInstruction.Mnemonic == Mnemonic.Push)
            //    stackOffset -= Is32Bit ? 4u : 8u;
            // else if (nodeInstruction.Mnemonic == Mnemonic.Pop)
            //    stackOffset += Is32Bit ? 4u : 8u;
            // else if (nodeInstruction.Mnemonic == Mnemonic.Add && nodeInstruction.Op0Register.GetFullRegister() == Register.RSP && nodeInstruction.Op1Kind == // Some Immediate)
            
        }
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
        var currentNode = new X86ControlFlowGraphNode() {ID = idCounter++};
        AddNode(currentNode);
        AddDirectedEdge(Root, currentNode);
        for (var i = 0; i < Instructions.Count; i++)
        {
            switch (Instructions[i].FlowControl)
            {
                case FlowControl.UnconditionalBranch:
                    currentNode.AddInstruction(Instructions[i]);
                    var newNodeFromJmp = new X86ControlFlowGraphNode() {ID = idCounter++};
                    AddNode(newNodeFromJmp);
                    var result = Instructions.Any(instruction => instruction.IP == Instructions[i].NearBranch64);
                    if (!result)
                        AddDirectedEdge(currentNode, newNodeFromJmp); // This is a jmp outside of this method, presumably a noreturn method or a tail call probably
                    else
                        currentNode.NeedsCorrectingDueToJump = true;
                    currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                    currentNode = newNodeFromJmp;
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

        for (var index = 0; index < Nodes.Count; index++)
        {
            var node = Nodes[index];
            if (node.NeedsCorrectingDueToJump)
                FixNode(node);
        }
    }
}
