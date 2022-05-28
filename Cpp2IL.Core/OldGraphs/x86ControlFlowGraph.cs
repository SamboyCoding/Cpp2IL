using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Il2CppApiFunctions;
using Iced.Intel;

namespace Cpp2IL.Core.Graphs;

public class X86ControlFlowGraph : AbstractControlFlowGraph<Instruction, X86ControlFlowGraphNode>
{
    public bool Is32Bit;
    public X86ControlFlowGraph(List<Instruction> instructions, bool is32Bit, BaseKeyFunctionAddresses keyFunctionAddresses) : base(instructions, keyFunctionAddresses)
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

    private void FixNode(X86ControlFlowGraphNode node, bool removeJmp = false)
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

        if (removeJmp)
            node.Instructions.Remove(jump);
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
    
    private static InstructionInfoFactory _instructionInfoFactory = new();
    private void TraverseNode(InstructionGraphNode<Instruction> node)
    {
        node.Visited = true;

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
            /*switch (nodeInstruction.Mnemonic)
            {
                case Mnemonic.Mov when nodeInstruction.Op0Kind == OpKind.Register && nodeInstruction.Op1Kind == OpKind.Register:
                    var op0 = InstructionSetIndependentOperand.MakeRegister(nodeInstruction.Op0Register.GetFullRegister().ToString());
                    var op1 = InstructionSetIndependentOperand.MakeRegister(nodeInstruction.Op1Register.GetFullRegister().ToString());
                    node.TranslatedInstructions.Add(new InstructionSetIndependentInstruction(InstructionSetIndependentOpCode.Move, op0, op1));
                    break;
                case Mnemonic.Mov when nodeInstruction.Op0Kind == OpKind.Register && nodeInstruction.Op1Kind == OpKind.Memory:
                    var op0Reg = InstructionSetIndependentOperand.MakeRegister(nodeInstruction.Op0Register.GetFullRegister().ToString());
                    var op1Mem = InstructionSetIndependentOperand.MakeMemory(
                        new IsilMemoryOperand(
                            InstructionSetIndependentOperand.MakeRegister(nodeInstruction.MemoryBase.GetFullRegister().ToString()), 
                            InstructionSetIndependentOperand.MakeRegister(nodeInstruction.MemoryIndex.GetFullRegister().ToString()),
                            (long)nodeInstruction.MemoryDisplacement64, 
                            nodeInstruction.MemoryIndexScale
                            )
                        );
                    node.TranslatedInstructions.Add(new InstructionSetIndependentInstruction(InstructionSetIndependentOpCode.Move, op0Reg, op1Mem));
                    break;
                case Mnemonic.Mov when nodeInstruction.Op0Kind == OpKind.Memory && nodeInstruction.Op1Kind.IsImmediate():
                    var op0Mem = InstructionSetIndependentOperand.MakeMemory(
                        new IsilMemoryOperand(
                            InstructionSetIndependentOperand.MakeRegister(nodeInstruction.MemoryBase.GetFullRegister().ToString()), 
                            InstructionSetIndependentOperand.MakeRegister(nodeInstruction.MemoryIndex.GetFullRegister().ToString()),
                            (long)nodeInstruction.MemoryDisplacement64, 
                            nodeInstruction.MemoryIndexScale
                        )
                    );
                    node.TranslatedInstructions.Add(new InstructionSetIndependentInstruction(InstructionSetIndependentOpCode.Move, op0Mem, InstructionSetIndependentOperand.MakeImmediate(nodeInstruction.GetImmediate(1))));
                    break;
                case Mnemonic.Call:
                    node.TranslatedInstructions.Add(new InstructionSetIndependentInstruction(InstructionSetIndependentOpCode.Call));
                    break;
            }*/
            
        }
    }
    
    protected override void GetUseDefsForInstruction(Instruction instruction, InstructionGraphUseDef instructionGraphUseDef)
    {
        var info = _instructionInfoFactory.GetInfo(instruction);
        foreach (var usedRegister in info.GetUsedRegisters())
        {
            switch (usedRegister.Access)
            {
                case OpAccess.CondRead:
                case OpAccess.Read:
                    instructionGraphUseDef.Uses.Add(usedRegister.Register.GetFullRegister().ToString());
                    break;
                case OpAccess.Write:
                case OpAccess.CondWrite:
                    instructionGraphUseDef.Definitions.Add(usedRegister.Register.GetFullRegister().ToString());
                    break;
                case OpAccess.ReadCondWrite:
                case OpAccess.ReadWrite:
                    var item = usedRegister.Register.GetFullRegister().ToString();
                    instructionGraphUseDef.Uses.Add(item);
                    instructionGraphUseDef.Definitions.Add(usedRegister.Register.GetFullRegister().ToString());
                    break;
            }
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
     
            var isLast = i == Instructions.Count - 1;
            switch (Instructions[i].FlowControl)
            {
                case FlowControl.UnconditionalBranch:
                    currentNode.AddInstruction(Instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromJmp = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromJmp);
                        var result = Instructions.Any(instruction =>
                            instruction.IP == Instructions[i].NearBranchTarget);
                        if (!result)
                        {
                            //AddDirectedEdge(currentNode, newNodeFromJmp); // This is a jmp outside of this method, presumably a noreturn method or a tail call probably
                            AddDirectedEdge(currentNode, ExitNode);
                        }
                        else
                            currentNode.NeedsCorrectingDueToJump = true;

                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromJmp;
                    }
                    else
                    {
                        AddDirectedEdge(currentNode, ExitNode);
                        currentNode.NeedsCorrectingDueToJump = true;
                    }
                    break;
                case FlowControl.IndirectCall:
                case FlowControl.Call:
                    currentNode.AddInstruction(Instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromCall = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromCall);
                        AddDirectedEdge(currentNode, newNodeFromCall);
                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromCall;
                    }
                    else
                    {
                        AddDirectedEdge(currentNode, ExitNode);
                    }
                    break;
                case FlowControl.Next:
                    currentNode.AddInstruction(Instructions[i]);
                    if (isLast) { /* This shouldn't happen */}
                    break;
                case FlowControl.Return:
                    currentNode.AddInstruction(Instructions[i]);
                    var newNodeFromReturn = new X86ControlFlowGraphNode() {ID = idCounter++};
                    AddNode(newNodeFromReturn);
                    AddDirectedEdge(currentNode, ExitNode);
                    currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                    currentNode = newNodeFromReturn;
                    break;
                case FlowControl.ConditionalBranch:
                    currentNode.AddInstruction(Instructions[i]);
                    if (!isLast)
                    {
                        var newNodeFromConditionalBranch = new X86ControlFlowGraphNode() {ID = idCounter++};
                        AddNode(newNodeFromConditionalBranch);
                        AddDirectedEdge(currentNode, newNodeFromConditionalBranch);
                        currentNode.FlowControl = GetAbstractControlFlow(Instructions[i].FlowControl);
                        currentNode = newNodeFromConditionalBranch;
                    }
                    else
                    {
                        AddDirectedEdge(currentNode, ExitNode);
                    }

                    break;
                case FlowControl.Interrupt:
                    currentNode.AddInstruction(Instructions[i]);
                    var newNodeFromInterrupt = new X86ControlFlowGraphNode() {ID = idCounter++};
                    AddNode(newNodeFromInterrupt);
                    AddDirectedEdge(currentNode, ExitNode);
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
                FixNode(node, true);
        }
        
        //CleanUp();
    }
    
    private void CleanUp()
    {
        var nodesToRemove = new List<X86ControlFlowGraphNode>();
        foreach (var node in Nodes)
        {
            if (node.Successors.Count == 0 && node.Predecessors.Count == 0) 
                nodesToRemove.Add(node);
        }
        foreach (var node in nodesToRemove)
        {
            Nodes.Remove(node);
        }
    }
}
