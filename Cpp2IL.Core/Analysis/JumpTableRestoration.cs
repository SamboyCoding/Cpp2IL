using System.Collections.Generic;
using System.Buffers.Binary;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Finds the address calculation used by relative switch jump tables.
/// </summary>
public static class JumpTableRestoration
{
    public static void Run(MethodAnalysisContext method) {
        var candidates = FindCandidates(method.ControlFlowGraph!);
        
        foreach (var candidate in candidates)
        {
            if (candidate.TryRestore(method.AppContext, method.ControlFlowGraph!)) {}
                //Logger.InfoNewline($"Restored {candidate.TableLength}-entry jump table at {candidate.LoadBase.Index} in {method.FullName}");
        }
    }

    public static List<JumpTableCandidate> FindCandidates(ISILControlFlowGraph cfg)
    {
        var candidates = new List<JumpTableCandidate>();

        foreach (var block in cfg.Blocks)
        {
            for (var index = 0; index <= block.Instructions.Count - 4; index++)
            {
                if (TryMatch(block, index, out var candidate))
                    candidates.Add(candidate);
            }
        }

        return candidates;
    }
    
    
    /// <summary>
    /// Looks for x86 msvc compiler generated jump table patterns
    /// TODO: Abstract this in some way so that we can have a separate implementation for gcc etc.
    /// </summary>
    private static bool TryMatch(Block dispatchBlock, int index, out JumpTableCandidate candidate)
    {
        candidate = default;

        var instructions = dispatchBlock.Instructions;

        var loadBase = instructions[index];
        var loadOffset = instructions[index + 1];
        var addBase = instructions[index + 2];
        var jump = instructions[index + 3];

        if (loadBase is not { OpCode: OpCode.Move, Operands.Count: 2 } ||
            //if (index != 0 || instructions.Count != 4 ||
            //    loadBase is not { OpCode: OpCode.Move, Operands.Count: 2 } ||
            loadBase.Destination is not Register baseRegister ||
            loadBase.Operands[1] is not Immediate tableBase ||
            loadOffset is not { OpCode: OpCode.Move, Operands.Count: 2 } ||
            loadOffset.Destination is not Register offsetRegister ||
            loadOffset.Operands[1] is not MemoryOperand tableOffset ||
            !Equals(tableOffset.Base, baseRegister) ||
            addBase is not { OpCode: OpCode.Add, Operands.Count: 3 } ||
            addBase.Destination is not Register targetRegister ||
            !targetRegister.Equals(offsetRegister) ||
            !Equals(addBase.Operands[1], offsetRegister) ||
            !Equals(addBase.Operands[2], baseRegister) ||
            jump is not { OpCode: OpCode.IndirectJump, Operands.Count: > 0 } ||
            !Equals(jump.Operands[0], targetRegister))
        {
            return false;
        }

        if (dispatchBlock.Predecessors is not [{ BlockType: BlockType.TwoWay } boundsBlock] ||
            boundsBlock.Instructions.Count == 0 ||
            boundsBlock.Instructions[^1] is not { OpCode: OpCode.ConditionalJump } boundsJump ||
            !boundsBlock.Successors.Contains(dispatchBlock) ||
            boundsJump.Operands[0] is not Block branchTarget ||
            ReferenceEquals(branchTarget, dispatchBlock) ||
            !TryGetTableLength(boundsBlock, boundsJump, out var boundsCheck, out var tableIndex, out var tableLength))
        {
            return false;
        }

        candidate = new JumpTableCandidate(
            dispatchBlock, loadBase, loadOffset, addBase, jump, tableBase, tableOffset,
            boundsCheck, tableIndex, tableLength);
        return true;
    }

    private static bool TryGetTableLength(Block boundsBlock, Instruction boundsJump, out Instruction boundsCheck,
        out IOperand tableIndex, out int tableLength)
    {
        boundsCheck = null!;
        tableIndex = null!;
        tableLength = 0;

        if (boundsJump.Operands.Count < 2 || boundsJump.Operands[1] is not Register condition)
            return false;

        var instructions = boundsBlock.Instructions;
        if (!TryGetDefinition(condition, instructions, instructions.Count, OpCode.And, out var greaterThan) ||
            !TryGetGreaterThanOperands(greaterThan, instructions, out tableIndex, out var upperBound))
        {
            return false;
        }

        if (upperBound is not Immediate { Value: >= 0 and < int.MaxValue } bound)
            return false;

        boundsCheck = greaterThan;
        tableLength = checked((int)bound.Value + 1);
        return true;
    }

    private static bool TryGetGreaterThanOperands(Instruction condition, IReadOnlyList<Instruction> instructions,
        out IOperand index, out IOperand upperBound)
    {
        index = upperBound = null!;

        if (condition.Operands.Count != 3 ||
            condition.Operands[1] is not Register left ||
            condition.Operands[2] is not Register right)
        {
            return false;
        }

        var before = GetInstructionIndex(instructions, condition);
        return TryGetSignEqualsOverflow(left, instructions, before, out index, out upperBound) &&
               IsNotZeroFlag(right, instructions, before) ||
               TryGetSignEqualsOverflow(right, instructions, before, out index, out upperBound) &&
               IsNotZeroFlag(left, instructions, before);
    }

    private static bool TryGetSignEqualsOverflow(Register condition, IReadOnlyList<Instruction> instructions, int before,
        out IOperand index, out IOperand upperBound)
    {
        index = upperBound = null!;

        if (!TryGetDefinition(condition, instructions, before, OpCode.CheckEqual, out var signEqualsOverflow) ||
            signEqualsOverflow.Operands.Count != 3)
        {
            return false;
        }

        foreach (var operand in new[] { signEqualsOverflow.Operands[1], signEqualsOverflow.Operands[2] })
        {
            if (operand is Register signFlag && TryGetSignFlagOperands(signFlag, instructions,
                    GetInstructionIndex(instructions, signEqualsOverflow), out index, out upperBound))
                return true;
        }

        return false;
    }

    private static bool IsNotZeroFlag(Register condition, IReadOnlyList<Instruction> instructions, int before)
    {
        return TryGetDefinition(condition, instructions, before, OpCode.Not, out var notZero) &&
               notZero.Operands.Count == 2 &&
               notZero.Operands[1] is Register zeroFlag &&
               TryGetDefinition(zeroFlag, instructions, GetInstructionIndex(instructions, notZero), OpCode.CheckEqual, out var checkZero) &&
               checkZero.Operands.Count == 3 &&
               checkZero.Operands[1] is Register subtraction &&
               checkZero.Operands[2] is Immediate { Value: 0 } &&
               TryGetDefinition(subtraction, instructions, GetInstructionIndex(instructions, checkZero), OpCode.Subtract, out _);
    }

    private static bool TryGetSignFlagOperands(Register condition, IReadOnlyList<Instruction> instructions, int before,
        out IOperand index, out IOperand upperBound)
    {
        index = upperBound = null!;

        if (!TryGetDefinition(condition, instructions, before, OpCode.CheckLess, out var signFlag) ||
            signFlag.Operands.Count != 3 ||
            signFlag.Operands[1] is not Register subtraction ||
            signFlag.Operands[2] is not Immediate { Value: 0 } ||
            !TryGetDefinition(subtraction, instructions, GetInstructionIndex(instructions, signFlag), OpCode.Subtract, out var subtract) ||
            subtract.Operands.Count != 3)
        {
            return false;
        }

        index = subtract.Operands[1];
        upperBound = subtract.Operands[2];
        return true;
    }

    private static bool TryGetDefinition(Register register, IReadOnlyList<Instruction> instructions, int before, OpCode opCode,
        out Instruction instruction)
    {
        for (var index = before - 1; index >= 0; index--)
        {
            if (instructions[index].Destination is not Register destination || !destination.Equals(register))
                continue;

            instruction = instructions[index];
            return instruction.OpCode == opCode;
        }

        instruction = null!;
        return false;
    }

    private static int GetInstructionIndex(IReadOnlyList<Instruction> instructions, Instruction instruction)
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            if (ReferenceEquals(instructions[index], instruction))
                return index;
        }

        return -1;
    }
}

public readonly record struct JumpTableCandidate(
    Block DispatchBlock,
    Instruction LoadBase,
    Instruction LoadOffset,
    Instruction AddBase,
    Instruction Jump,
    Immediate TableBase,
    MemoryOperand TableOffset,
    Instruction BoundsCheck,
    IOperand TableIndex,
    int TableLength)
{
    /// <summary>
    /// The address of the first jump table entry. The index and scale select an entry within it.
    /// </summary>
    public ulong TableAddress => checked((ulong)checked(TableBase.Value + TableOffset.Addend));

    /// <summary>
    /// Reads the signed 32-bit relative offsets stored in this jump table.
    /// </summary>
    public bool TryReadEntries(ApplicationAnalysisContext appContext, out int[] entries)
    {
        entries = [];

        if (TableOffset.Scale != sizeof(int) ||
            !appContext.Binary.TryMapVirtualAddressToRaw(TableAddress, out var rawAddress))
        {
            return false;
        }

        var byteLength = checked((long)TableLength * sizeof(int));
        var content = appContext.Binary.GetRawBinaryContent();
        if (rawAddress < 0 || byteLength > content.Length || rawAddress > content.Length - byteLength)
            return false;

        entries = new int[TableLength];
        var tableBytes = content.Slice((int)rawAddress, (int)byteLength);
        for (var index = 0; index < entries.Length; index++)
            entries[index] = BinaryPrimitives.ReadInt32LittleEndian(tableBytes.Slice(index * sizeof(int), sizeof(int)));

        return true;
    }

    /// <summary>
    /// Replaces the relative-offset dispatch sequence with a switch whose ordered targets correspond to table entries.
    /// </summary>
    public bool TryRestore(ApplicationAnalysisContext appContext, ISILControlFlowGraph cfg)
    {
        if (!TryReadEntries(appContext, out var entries))
            return false;

        var instructions = cfg.Blocks.SelectMany(block => block.Instructions).ToList();
        var targetInstructions = new List<Instruction>(entries.Length);
        foreach (var entry in entries)
        {
            var targetIp = checked((ulong)checked(TableBase.Value + entry));
            var targetInstruction = instructions.FirstOrDefault(instruction => instruction.IP == targetIp);
            if (targetInstruction == null || DispatchBlock.Instructions.Contains(targetInstruction))
                return false;

            targetInstructions.Add(targetInstruction);
        }

        var targetBlocks = new List<Block>(targetInstructions.Count);
        foreach (var targetInstruction in targetInstructions)
        {
            var targetBlock = cfg.GetOrSplitBlockForInstruction(targetInstruction);
            if (targetBlock == null)
                return false;

            targetBlocks.Add(targetBlock);
        }

        DispatchBlock.Instructions.Clear();
        DispatchBlock.AddInstruction(new Instruction(Jump.Index, OpCode.Switch,
            TableIndex, new SwitchTargets(targetBlocks)) { IP = Jump.IP });
        cfg.ReplaceSuccessors(DispatchBlock, targetBlocks);
        DispatchBlock.CalculateBlockType();
        return true;
    }
}
