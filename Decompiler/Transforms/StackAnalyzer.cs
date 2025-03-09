using System.Diagnostics;
using Decompiler.ControlFlow;
using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Analyzes the stack and replaces it with registers.
/// Taken from https://github.com/SamboyCoding/Cpp2IL/blob/development-gompo-ast/Cpp2IL.Core/Graphs/Analysis/Stack/StackAnalyzer.cs
/// </summary>
public class StackAnalyzer : ITransform
{
    [DebuggerDisplay("Size = {Size}")]
    private class StackEntry
    {
        public int Size;
        public StackEntry Copy() => new() { Size = this.Size };
    }

    private HashSet<Block> _visited = [];
    private Dictionary<Block, StackEntry> _inComingDelta = [];
    private Dictionary<Block, StackEntry> _outGoingDelta = [];
    private Dictionary<Instruction, StackEntry> _instructionsState = [];

    public void Apply(Method method)
    {
        _visited.Clear();
        _inComingDelta.Clear();
        _outGoingDelta.Clear();
        _instructionsState.Clear();

        var graph = method.ControlFlowGraph;

        _inComingDelta = new Dictionary<Block, StackEntry>() { { graph.EntryBlock, new StackEntry() } };

        TraverseGraph(graph.EntryBlock, graph, method);

        var outDelta = _outGoingDelta[graph.ExitBlock];
        if (outDelta.Size != 0)
        {
            var outText = outDelta.Size < 0 ? "-" + (-outDelta.Size).ToString("X") : outDelta.Size.ToString("X");
            method.AddWarning($"Method ends with non empty stack! ({outText})");
        }

        foreach (var block in graph.Blocks)
        {
            Instruction? previous = null;

            foreach (var instruction in block.Instructions)
            {
                /*
                 *   Push/Pop
                 *       ShiftStack -operandSize
                 *       Move stack[0], operand
                 */
                if (instruction.OpCode == OpCode.ShiftStack)
                {
                    // Nop the shift stack instruction
                    instruction.OpCode = OpCode.Nop;
                    instruction.Operands = [];

                    // Correct stack offset for previous move instruction if it matches (push/pop combo)
                    if (previous is { OpCode: OpCode.Move } && previous.Operands[0] is StackOffsetOperand offset)
                    {
                        if (offset.Offset != 0) continue;

                        var currentPos = _instructionsState[instruction].Size;
                        previous.Operands[1] = new StackOffsetOperand(currentPos);
                    }
                }

                previous = instruction;
            }

            if (!block.IsCall) continue;

            // Correct offsets for call params
            var callInstruction = block.Instructions.Last();
            var stackSize = _instructionsState[callInstruction].Size;

            for (var i = 0; i < callInstruction.Operands.Count; i++)
            {
                var op = callInstruction.Operands[i];
                if (op == null) continue;

                if (op.Type == OperandType.StackOffset)
                {
                    var actual = stackSize - ((StackOffsetOperand)op).Offset;
                    callInstruction.Operands[i] = new StackOffsetOperand(actual);
                }
            }
        }

        graph.MergeCallBlocks();

        ReplaceStackWithRegisters(method);
    }

    // Traverse the graph and calculate the stack state for each block and instruction
    private void TraverseGraph(Block block, ControlFlowGraph graph, Method method)
    {
        var blockDelta = _inComingDelta[block].Copy();

        var previous = blockDelta;

        foreach (var instruction in block.Instructions)
        {
            _instructionsState[instruction] = previous;

            if (instruction.OpCode != OpCode.ShiftStack) continue;
            var offset = ((IntOperand)instruction.Operands[0]!).Value;

            previous = previous.Copy();

            // Change stack state
            previous.Size += offset;
        }

        blockDelta = previous;

        // Tail call
        if (block is { IsCall: true, Successors.Count: 1 } && block.Successors[0] == graph.ExitBlock)
            blockDelta.Size = 0;

        _outGoingDelta[block] = blockDelta;

        // Traverse successors
        foreach (var successor in block.Successors)
        {
            if (!_visited.Contains(successor))
            {
                _inComingDelta[successor] = blockDelta;
                _visited.Add(successor);
                TraverseGraph(successor, graph, method);
            }
            else
            {
                var expectedDelta = _inComingDelta[successor];

                if (expectedDelta.Size != blockDelta.Size)
                {
                    var expectedText = expectedDelta.Size < 0 ? "-" + (-expectedDelta.Size).ToString("X") : expectedDelta.Size.ToString("X");
                    var actualText = blockDelta.Size < 0 ? "-" + (-blockDelta.Size).ToString("X") : blockDelta.Size.ToString("X");
                    method.AddWarning($"Unbalanced stack! expected: {expectedText}, actual: {actualText}");
                }

                _inComingDelta[successor] = blockDelta;
            }
        }
    }

    private static void ReplaceStackWithRegisters(Method method)
    {
        // Get all offsets without duplicates
        var offsets = new List<int>();
        foreach (var operand in method.Instructions.SelectMany(instruction => instruction.Operands))
        {
            if (operand is StackOffsetOperand offset)
            {
                if (!offsets.Contains(offset.Offset))
                    offsets.Add(offset.Offset);
            }
        }

        // Get max register number
        var maxRegisterNumber = 0;
        foreach (var operand in method.Instructions.SelectMany(instruction => instruction.Operands))
        {
            if (operand is RegisterOperand register)
            {
                if (register.Number > maxRegisterNumber)
                    maxRegisterNumber = register.Number;
            }
        }

        // Map offsets to registers
        var offsetToRegister = new Dictionary<int, int>();
        for (var i = 0; i < offsets.Count; i++)
        {
            var offset = offsets[i];
            offsetToRegister.Add(offset, maxRegisterNumber + i + 1);
        }

        // Replace stack offset operands
        foreach (var instruction in method.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is StackOffsetOperand offset)
                {
                    var name = offset.Offset < 0 ? "m" + (-offset.Offset).ToString("X") : offset.Offset.ToString("X");
                    instruction.Operands[i] = new RegisterOperand(offsetToRegister[offset.Offset], $"stack_{name}");
                }
            }
        }
    }
}
