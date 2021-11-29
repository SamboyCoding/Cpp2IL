using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Cpp2IL.Core;

public class AbstractControlFlowGraph<TInstruction, TNode> where TNode : InstructionGraphNode<TInstruction>, new()
{
        protected List<TInstruction> Instructions;

        protected TNode  EndNode;
        private TNode  InterruptNode; // TODO: Remove this, replace with normal end node
        Dictionary<ulong, TInstruction> InstructionsByAddress;
        protected int idCounter = 0;

        protected AbstractControlFlowGraph(List<TInstruction> instructions)
        {
            if (instructions == null)
                throw new ArgumentNullException(nameof(instructions));


            var startNode = new TNode() {ID = idCounter++};
            EndNode = new TNode() {ID = idCounter++};
            InterruptNode = new TNode() {ID = idCounter++};
            Instructions = instructions;
            InstructionsByAddress = new Dictionary<ulong, TInstruction>();
            Root = startNode;
            nodeSet = new Collection<TNode>();
            
            foreach (var instruction in Instructions)
                InstructionsByAddress.Add(GetAddressOfInstruction(instruction), instruction);
            
        }

        protected virtual ulong GetAddressOfInstruction(TInstruction instruction)
        {
            throw new NotImplementedException();
        }

        protected TNode? SplitAndCreate(TNode target, int index, int id)
        {
            if(index < 0 || index >= target.Instructions.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (index == 0)
                return null;

            var newNode = new TNode(){ID = id};
            
            var instructions = target.Instructions.GetRange(index, target.Instructions.Count - index);
            target.Instructions.RemoveRange(index, target.Instructions.Count - index);
            newNode.Instructions.AddRange(instructions);
            newNode.FlowControl = target.FlowControl;
            target.FlowControl = InstructionGraphNodeFlowControl.Continue;
            newNode.Neighbors = target.Neighbors;
            
            target.Neighbors = new Collection<TNode>();

            return newNode;
        }
        

        public void Run()
        {
            AddNode(Root);
            if (Instructions.Count == 0)
            {
                AddNode(EndNode); 
                AddDirectedEdge(Root, EndNode);
                return;
            }
            BuildInitialGraph();
            AddNode(EndNode);
            AddNode(InterruptNode);
            SegmentGraph();
            ConstructConditions();
            Print();
            ExtractFeatures();
        }

        protected virtual void ExtractFeatures()
        {
            throw new NotImplementedException();
        }

        private void ConstructConditions()
        {
            foreach (var graphNode in Nodes)
            {
                if (graphNode.IsCondtionalBranch)
                {
                    graphNode.CheckCondition();
                }
            }
        }

        protected virtual void SegmentGraph()
        {
            throw new NotImplementedException();
        }

        protected virtual void BuildInitialGraph()
        {
            throw new NotImplementedException();
        }

        protected TNode? FindNodeByAddress(ulong address)
        {
            if (InstructionsByAddress.TryGetValue(address, out var instruction))
            {
                foreach (var node in Nodes)
                {
                    if (node.Instructions.Any(instr => GetAddressOfInstruction(instr) == address))
                    {
                        return node;
                    }
                }
            }
            return null;
        }
    public TNode Root { get; }
        
    private List<TNode> nodeSet;

    protected void AddDirectedEdge(TNode from, TNode to)
    {
        from.Neighbors.Add(to);
    }

    protected void AddNode(TNode node) => nodeSet.Add(node);
    
        
    public void Print()
    {
        foreach (var node in nodeSet)
        {
            Console.WriteLine("=========================");
            Console.Write($"ID: {node.ID}, FC: {node.FlowControl}, Successors:{string.Join(",", node.Neighbors.Select(i => i.ID))}");
            if(node.IsCondtionalBranch)
                Console.Write($", Condition: {node.Condition?.ConditionString ?? "Null"}");
            Console.Write("\n");
            foreach (var v in node.Instructions)
            {
                Console.WriteLine(v.ToString());
            }
            Console.WriteLine();
        }
    }

    protected List<TNode> Nodes => nodeSet;

    public int Count => nodeSet.Count;
}