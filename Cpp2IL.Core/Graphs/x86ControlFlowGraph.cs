using System;
using System.Collections.Generic;
using System.Linq;
using Iced.Intel;

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
    private void TraverseNode(X86ControlFlowGraphNode node)
    {
        uint stackOffset = 0;
        // Plan is to have a stack offset for start of every node. 
        // We'd also like to figure out which registers are used and set. If GetUsedRegisters() is an actual Iced method, then it would simplify this section a lot.
        // Otherwise we'll just need to make sure we cover all bases with all the common instructions out there used in games
        for (int i = 0; i < node.Instructions.Count; i++)
        {
            var nodeInstruction = node.Instructions[i];
            switch (nodeInstruction.Mnemonic)
            {
                case Mnemonic.Add:   // Normal add
                case Mnemonic.Addsd: // Double add
                case Mnemonic.Addss: // Single add
                case Mnemonic.And:   // And two regs
                case Mnemonic.Andps: // Packed single And
                case Mnemonic.Bt:    // Bit test and set carry to bit
                case Mnemonic.Btr:   // Bit test, set carry to bit and clear bit 
                case Mnemonic.Bts:   // Bit test, set carry to bit and set bit 
                case Mnemonic.Cmova: // Move if above (unsigned)
                case Mnemonic.Cmovae:// Move if above or equal (unsigned)
                case Mnemonic.Cmovb: // Move if below (unsigned)
                case Mnemonic.Cmovbe:// Move if below or equal (unsigned)
                case Mnemonic.Cmove: // Move if equal
                case Mnemonic.Cmovne:// Move if not equal
                case Mnemonic.Cmovg: // Move if greater (signed)
                case Mnemonic.Cmovge:// Move if greater or equal (signed)
                case Mnemonic.Cmovl: // Move if less (signed)
                case Mnemonic.Cmovle:// Move if less or equal (signed)
                case Mnemonic.Cmovs: // Move if negative
                case Mnemonic.Cmovns:// Move if not negative
                //case Mnemonic.Cvtdq2pd: // Convert two, four or eight packed double word integer to packed double-precision floating-point
                //case Mnemonic.Cvtdq2ps: // Convert four, eight or sixteen packed double word integer to four, eight or sixteen packed single-precision floating-point
                //case Mnemonic.Cvtpd2ps: // Converts two, four or eight packed double-precision floating-point values in the source operand (second operand) to two, four or eight packed single-precision floating-point values in the destination operand (first operand).
                //case Mnemonic.Cvtps2pd: // Converts two, four or eight packed single-precision floating-point values in the source operand (second operand) to two, four or eight packed double-precision floating-point values in the destination operand (first operand).
                //case Mnemonic.Cvtsd2ss: // Converts a double-precision floating-point value in the “convert-from” source operand to a single-precision floating-point value in the destination operand.
                //case Mnemonic.Cvtsi2sd: // Converts a signed doubleword integer (or signed quadword integer if operand size is 64 bits) in the “convert-from” source operand to a double-precision floating-point value in the destination operand
                //case Mnemonic.Cvtsi2ss: // Convert Doubleword Integer to Scalar Single-Precision Floating-Point Value
                //case Mnemonic.Cvtss2sd: // Convert Scalar Single-Precision Floating-Point Value to Scalar Double-Precision Floating-Point Value
                //case Mnemonic.Cvttsd2si: // Convert with Truncation Scalar Double-Precision Floating-Point Value to Signed Integer
                //case Mnemonic.Cvttss2si: // Convert with Truncation Scalar Single-Precision Floating-Point Value to Signed Integer    
                //case Mnemonic.Divps:     // Performs a SIMD divide of the four, eight or sixteen packed single-precision floating-point values in the first source operand (the second operand) by the four, eight or sixteen packed single-precision floating-point values in the second source operand (the third operand). Results are written to the destination operand (the first operand).
                //case Mnemonic.Divss:     // Divides the low single-precision floating-point value in the first source operand by the low single-precision floating-point value in the second source operand, and stores the single-precision floating-point result in the destination operand  
                //case Mnemonic.Sub:       // Subtract
                //case Mnemonic.Xor:       // Xor
                //case Mnemonic.Lea:    

                        // Register Used and Set
                        // Registers used
                
                    break;
                case Mnemonic.Call: // Call
                case Mnemonic.Je:   // Jump if equal
                case Mnemonic.Jne:  // Jump if not equal
                case Mnemonic.Jmp:
                case Mnemonic.Ja:
                case Mnemonic.Jae:
                case Mnemonic.Jb:
                case Mnemonic.Jbe:    
                case Mnemonic.Push:    
                    // 1 reg used
                    break;
                case Mnemonic.Cmp:  // Comparison
                case Mnemonic.Comisd: // Compare double
                case Mnemonic.Comiss: // Compare single  
                    // 2 reg used
                    break;
                case Mnemonic.Cdq:  // Sign-extends EAX into EDX
                    break;
                case Mnemonic.Cwde: // EAX ← sign-extend of AX.
                case Mnemonic.Cdqe: // Sign-extends EAX into RAX.
                    break;
                case Mnemonic.Cqo:  // Sign-extends RAX into RDX:RAX. 
                    break;
                case Mnemonic.Dec:  // Decrement first operand by 1
                    break;
                case Mnemonic.Div: // Divides unsigned value in RDX:RAX by source operand and stores quotient in RAX and remainder in RDX
                case Mnemonic.Idiv: // Divides unsigned value in RDX:RAX by source operand and stores quotient in RAX and remainder in RDX
                    break;
                
                case Mnemonic.Imul:
                    if (nodeInstruction.OpCount == 1)
                    {
                        // The source operand is multiplied by the value in the AL, AX, EAX, or RAX register and the product (twice the size of the input operand) is stored in the AX, DX:AX, EDX:EAX, or RDX:RAX registers, respectively.
                    }
                    else if (nodeInstruction.OpCount == 2)
                    {
                        
                    }
                    else if (nodeInstruction.OpCount == 3)
                    {
                        
                    }
                    else
                    {
                        throw new NotImplementedException("IMul with " + nodeInstruction.OpCount.ToString());
                    }
                    break;
                case Mnemonic.Ret: // Return
                case Mnemonic.Int: // Kaboom?
                    // No regs used
                    break;
                case Mnemonic.Pop:
                    // Uses and sets one reg
                    break;
                



            }
            // Crude stack calculation. 
            if (nodeInstruction.Mnemonic == Mnemonic.Push)
                stackOffset += Is32Bit ? 4u : 8u;
            else if (nodeInstruction.Mnemonic == Mnemonic.Pop)
                stackOffset -= Is32Bit ? 4u : 8u;
            //else if (nodeInstruction.Mnemonic == Mnemonic.Add && nodeInstruction.Op0Register.GetFullRegister() == Register.RSP && nodeInstruction.Op1Kind == // Some Immediate)
            
        }
        node.HasProcessedInstructions = true;
        foreach (var succ in node.Successors)
        {
            if (!succ.HasProcessedInstructions)
            {
                TraverseNode(node);
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
