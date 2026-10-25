using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Analysis;

// Turns the raw Il2CppArray layout (header, then length, then inline elements) back into array ops
public static class ArrayRecovery
{
    private static readonly HashSet<string> ArrayNewFunctions =
    [
        "SzArrayNew",
        "il2cpp_vm_array_new_specific",
        "il2cpp_array_new_specific",
    ];

    // Il2CppArray is {Il2CppObject obj; void* bounds; il2cpp_array_size_t max_length;} then the elements, on all versions(?)
    private static long LengthOffset(int pointerSize) => 3L * pointerSize;
    private static long ElementsOffset(int pointerSize) => 4L * pointerSize;

    public static void Run(MethodAnalysisContext method)
    {
        RecoverAccesses(method);
        RecoverStructElementAddresses(method);
        GroupInitialisers(method.ControlFlowGraph!);
    }

    private static void RecoverAccesses(MethodAnalysisContext method)
    {
        var pointerSize = method.AppContext.Binary.PointerSizeBytes;

        // Track array pointer derivations (e.g. ptr = array + 32, or ptr2 = ptr + 8)
        var arrayPointers = new Dictionary<LocalVariable, (LocalVariable Array, SzArrayTypeAnalysisContext ArrayType, long Offset)>();
        var indexedArrayPointers = new Dictionary<LocalVariable, (LocalVariable Array, SzArrayTypeAnalysisContext ArrayType, IOperand Index, int Scale)>();
        var pointerDefs = new Dictionary<LocalVariable, Instruction>();
        var convertedPointers = new HashSet<LocalVariable>();

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            RecoverAllocation(instruction);

            if (instruction.OpCode == OpCode.Add && instruction.Operands.Count >= 3 && instruction.Operands[0] is LocalVariable dest)
            {
                LocalVariable? arrLocal = null;
                SzArrayTypeAnalysisContext? arrType = null;
                long baseOff = 0;
                long? immOffset = null;

                if (instruction.Operands[1] is LocalVariable l1 && instruction.Operands[2] is Immediate i2)
                {
                    immOffset = i2.Value;
                    if (l1.Type is SzArrayTypeAnalysisContext st)
                    {
                        arrLocal = l1;
                        arrType = st;
                        baseOff = 0;
                    }
                    else if (arrayPointers.TryGetValue(l1, out var parent))
                    {
                        arrLocal = parent.Array;
                        arrType = parent.ArrayType;
                        baseOff = parent.Offset;
                    }
                }
                else if (instruction.Operands[2] is LocalVariable l2 && instruction.Operands[1] is Immediate i1)
                {
                    immOffset = i1.Value;
                    if (l2.Type is SzArrayTypeAnalysisContext st)
                    {
                        arrLocal = l2;
                        arrType = st;
                        baseOff = 0;
                    }
                    else if (arrayPointers.TryGetValue(l2, out var parent))
                    {
                        arrLocal = parent.Array;
                        arrType = parent.ArrayType;
                        baseOff = parent.Offset;
                    }
                }
                else if (instruction.Operands[1] is LocalVariable vl1 && instruction.Operands[2] is LocalVariable vl2)
                {
                    // Variable index addition: ptr = array + (index << shift)
                    LocalVariable? candArr = vl1.Type is SzArrayTypeAnalysisContext ? vl1 : (vl2.Type is SzArrayTypeAnalysisContext ? vl2 : null);
                    LocalVariable? candIdx = ReferenceEquals(candArr, vl1) ? vl2 : (ReferenceEquals(candArr, vl2) ? vl1 : null);

                    if (candArr != null && candArr.Type is SzArrayTypeAnalysisContext st && candIdx != null)
                    {
                        IOperand realIndex = candIdx;
                        int scale = 1;

                        var shiftDef = method.ControlFlowGraph.Instructions.FirstOrDefault(i => i.OpCode == OpCode.ShiftLeft
                            && ReferenceEquals(i.Operands[0], candIdx) && i.Operands.Count >= 3 && i.Operands[2] is Immediate);

                        if (shiftDef != null && shiftDef.Operands[2] is Immediate shiftImm)
                        {
                            realIndex = shiftDef.Operands[1];
                            scale = 1 << (int)shiftImm.Value;
                        }

                        indexedArrayPointers[dest] = (candArr, st, realIndex, scale);
                        pointerDefs[dest] = instruction;
                    }
                }

                if (arrLocal != null && arrType != null && immOffset != null)
                {
                    arrayPointers[dest] = (arrLocal, arrType, baseOff + immOffset.Value);
                    pointerDefs[dest] = instruction;
                }
            }
            else if (instruction.OpCode == OpCode.Move && instruction.Operands.Count >= 2 && instruction.Operands[0] is LocalVariable mDest && instruction.Operands[1] is LocalVariable mSrc)
            {
                if (mSrc.Type is SzArrayTypeAnalysisContext st)
                {
                    arrayPointers[mDest] = (mSrc, st, 0);
                }
                else if (arrayPointers.TryGetValue(mSrc, out var parent))
                {
                    arrayPointers[mDest] = parent;
                }
            }
        }

        foreach (var instruction in method.ControlFlowGraph.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                if (instruction.Operands[i] is not MemoryOperand memory)
                    continue;

                LocalVariable? array = null;
                SzArrayTypeAnalysisContext? arrayType = null;
                long combinedAddend = memory.Addend;

                var baseLocal = memory.Base as LocalVariable;

                if (baseLocal != null && indexedArrayPointers.TryGetValue(baseLocal, out var idxInfo))
                {
                    var elemSize = ElementSize(idxInfo.ArrayType.ElementType, pointerSize);
                    if (elemSize > 0 && (idxInfo.Scale == elemSize || idxInfo.Scale == 1) && memory.Addend == ElementsOffset(pointerSize) && memory.Index == null)
                    {
                        instruction.SetOperand(i, new ArrayAccess(idxInfo.Array, idxInfo.Index));
                        convertedPointers.Add(baseLocal);
                        continue;
                    }
                }

                if (baseLocal?.Type is SzArrayTypeAnalysisContext at)
                {
                    array = baseLocal;
                    arrayType = at;
                }
                else if (baseLocal != null && arrayPointers.TryGetValue(baseLocal, out var ptrInfo))
                {
                    array = ptrInfo.Array;
                    arrayType = ptrInfo.ArrayType;
                    combinedAddend = ptrInfo.Offset + memory.Addend;
                }

                if (array == null || arrayType == null)
                    continue;

                if (memory.Index == null && memory.Scale == 0 && combinedAddend == LengthOffset(pointerSize))
                {
                    instruction.SetOperand(i, new ArrayLength(array));
                    continue;
                }

                var effectiveMem = new MemoryOperand(array, memory.Index, combinedAddend, memory.Scale);
                if (ElementIndex(effectiveMem, arrayType, pointerSize) is { } index)
                {
                    instruction.SetOperand(i, new ArrayAccess(array, index));
                    if (baseLocal != null && pointerDefs.ContainsKey(baseLocal))
                        convertedPointers.Add(baseLocal);
                }
            }
        }

        foreach (var ptr in convertedPointers)
        {
            if (pointerDefs.TryGetValue(ptr, out var defInst)
                && !method.ControlFlowGraph.Instructions.Any(inst => inst != defInst && inst.Operands.Contains(ptr)))
            {
                defInst.OpCode = OpCode.Nop;
                defInst.SetOperands();
            }

            if (indexedArrayPointers.TryGetValue(ptr, out var idxInfo) && idxInfo.Scale > 1)
            {
                var shiftDef = method.ControlFlowGraph.Instructions.FirstOrDefault(i => i.OpCode == OpCode.ShiftLeft
                    && i.Operands.Count >= 3 && Equals(i.Operands[1], idxInfo.Index)
                    && i.Operands[0] is LocalVariable shiftDest
                    && !method.ControlFlowGraph.Instructions.Any(inst => inst != i && inst.Operands.Contains(shiftDest)));

                if (shiftDef != null)
                {
                    shiftDef.OpCode = OpCode.Nop;
                    shiftDef.SetOperands();
                }
            }
        }
    }

    // Group initializers after an array allocation together so ILSpy decompiles them better
    private static void GroupInitialisers(ISILControlFlowGraph cfg)
    {
        var movedAny = false;

        foreach (var block in cfg.Blocks.ToList())
        {
            foreach (var allocation in block.Instructions.ToList())
            {
                if (allocation.OpCode != OpCode.NewArr || allocation.Operands[0] is not LocalVariable array)
                    continue;

                var stores = new List<(Block Block, Instruction Instruction)>();
                var current = block;
                var index = current.Instructions.IndexOf(allocation) + 1;
                var visitedBlocks = new HashSet<Block> { current };

                while (true)
                {
                    if (index >= current.Instructions.Count)
                    {
                        // only a straight-line run can be regrouped without changing what runs when
                        if (current.Successors.Count != 1 || current.Successors[0].Predecessors.Count != 1)
                            break;

                        var next = current.Successors[0];
                        if (!visitedBlocks.Add(next))
                            break;

                        current = next;
                        index = 0;
                        continue;
                    }

                    var instruction = current.Instructions[index];

                    if (IsElementStore(instruction, array))
                    {
                        stores.Add((current, instruction));
                        index++;
                        continue;
                    }

                    if (!ReadsArray(instruction, array))
                    {
                        index++;
                        continue;
                    }

                    // Found the first read. Move the allocation and its stores immediately in front, so the whole array is built in one chain with the elements already computed.
                    if (stores.Count > 1)
                    {
                        foreach (var (storeBlock, store) in stores)
                            storeBlock.Instructions.Remove(store);

                        block.Instructions.Remove(allocation);

                        var moved = new List<Instruction> { allocation };
                        moved.AddRange(stores.Select(s => s.Instruction));

                        current.Instructions.InsertRange(current.Instructions.IndexOf(instruction), moved);
                        movedAny = true;
                    }

                    break;
                }
            }
        }

        // Emptying a block out entirely leaves branches pointing at nothing to jump to
        if (movedAny)
            cfg.RemoveEmptyBlocks();
    }

    private static bool IsElementStore(Instruction instruction, LocalVariable array) =>
        instruction.OpCode == OpCode.Move && instruction.Operands[0] is ArrayAccess { Index: Immediate } stored
                                          && ReferenceEquals(stored.Array, array)
                                          && !ReadsArray(instruction, array);

    private static bool ReadsArray(Instruction instruction, LocalVariable array)
    {
        for (var i = 0; i < instruction.Operands.Count; i++)
        {
            if (i == 0 && instruction.OpCode == OpCode.Move)
                continue;

            var reads = instruction.Operands[i] switch
            {
                LocalVariable local => ReferenceEquals(local, array),
                ArrayAccess access => ReferenceEquals(access.Array, array),
                ArrayLength length => ReferenceEquals(length.Array, array),
                MemoryOperand memory => ReferenceEquals(memory.Base, array) || ReferenceEquals(memory.Index, array),
                AddressOf { Target: LocalVariable addressed } => ReferenceEquals(addressed, array),
                _ => false
            };

            if (reads)
                return true;
        }

        return false;
    }

    private static void RecoverAllocation(Instruction instruction)
    {
        // Call "SzArrayNew", result, typeof(T[]), length, ...
        if (!instruction.IsCall || instruction.Operands is not [StringLiteral { Value: var name }, LocalVariable result, TypeAnalysisContext type, { } length, ..]
            || !ArrayNewFunctions.Contains(name))
            return;

        instruction.OpCode = OpCode.NewArr;
        instruction.SetOperands(result, type, length);

        if (result.Type is not SzArrayTypeAnalysisContext)
            result.Type = type;
    }

    private static IOperand? ElementIndex(MemoryOperand memory, SzArrayTypeAnalysisContext arrayType, int pointerSize)
    {
        var elementSize = ElementSize(arrayType.ElementType, pointerSize);
        var offset = memory.Addend - ElementsOffset(pointerSize);

        if (offset < 0 || elementSize == 0 || offset % elementSize != 0)
            return null;

        if (memory.Index == null)
            return memory.Scale == 0 ? new Immediate(offset / elementSize) : null;

        return memory.Scale == elementSize && offset == 0 ? memory.Index : null;
    }

    private static long ElementSize(TypeAnalysisContext elementType, int pointerSize)
    {
        if (!elementType.IsValueType)
            return pointerSize;

        return elementType.FullName switch
        {
            "System.Boolean" or "System.Byte" or "System.SByte" => 1,
            "System.Int16" or "System.UInt16" or "System.Char" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            "System.IntPtr" or "System.UIntPtr" => pointerSize,
            _ => 0 // struct arrays are handled by the element-address path, which sizes them from metadata
        };
    }

    // Recovers &array[i] over struct arrays. Struct elements are never loaded outright, the compiler
    // computes their address via lea chains and calls through it. We solve the index as a linear function
    // of a local and demand an exact hit on the metadata stride, so a real load can't match by accident.
    private static void RecoverStructElementAddresses(MethodAnalysisContext method)
    {
        var pointerSize = method.AppContext.Binary.PointerSizeBytes;
        var cfg = method.ControlFlowGraph!;

        var definitions = SingleDefinitions(cfg);
        var uses = CollectUses(cfg);

        foreach (var instruction in cfg.Instructions)
        {
            if (instruction.IsCall)
            {
                for (var i = 1; i < instruction.Operands.Count; i++)
                {
                    if (MatchElementAddress(instruction.Operands[i], pointerSize, definitions) is { } inlined)
                        instruction.SetOperand(i, inlined);
                }

                continue;
            }

            // a Move from memory could be a load or a lifted lea, and only the uses tell them apart
            if (instruction is not { OpCode: OpCode.Move, Operands: [LocalVariable destination, MemoryOperand] })
                continue;

            if (definitions.TryGetValue(destination, out var single) && single == null)
                continue;

            if (!uses.TryGetValue(destination, out var destinationUses) || destinationUses.Count == 0
                || !destinationUses.All(u => u.Instruction.IsCall || IsMemoryBase(u.Instruction.Operands[u.OperandIndex], destination)))
                continue;

            if (MatchElementAddress(instruction.Operands[1], pointerSize, definitions) is { } address)
                instruction.SetOperand(1, address);
        }
    }

    private static AddressOf? MatchElementAddress(IOperand operand, int pointerSize, Dictionary<LocalVariable, Instruction?> definitions)
    {
        if (operand is not MemoryOperand memory
            || memory.Base is not LocalVariable { Type: SzArrayTypeAnalysisContext arrayType } array)
            return null;

        var elementType = arrayType.ElementType;
        if (!elementType.IsValueType || ElementSize(elementType, pointerSize) != 0)
            return null;

        var elementSize = MetadataElementSize(elementType, pointerSize);
        if (elementSize <= 0)
            return null;

        return StructElementIndex(memory, array, elementSize, pointerSize, definitions) is { } index
            ? new AddressOf(new ArrayAccess(array, index))
            : null;
    }

    private static bool IsMemoryBase(IOperand operand, LocalVariable local)
        => operand is MemoryOperand { Base: LocalVariable baseLocal } && ReferenceEquals(baseLocal, local);

    private static long MetadataElementSize(TypeAnalysisContext elementType, int pointerSize)
        => TypeSizes.UnboxedSize(elementType, pointerSize);

    private static IOperand? StructElementIndex(MemoryOperand memory, LocalVariable array, long elementSize, int pointerSize,
        Dictionary<LocalVariable, Instruction?> definitions)
    {
        var indexAffine = memory.Index is LocalVariable indexLocal
            ? ScaleBy(Evaluate(indexLocal, definitions, 0), Math.Max(memory.Scale, 1))
            : new Affine(null, 0, 0);

        if (Sum(indexAffine, new Affine(null, 0, memory.Addend)) is not { } address)
            return null;

        if (ReferenceEquals(address.Root, array))
            return null;

        var offset = address.Offset - ElementsOffset(pointerSize);

        if (address.Root != null)
            return address.Multiplier == elementSize && offset == 0 ? address.Root : null;

        return offset >= 0 && offset % elementSize == 0 ? new Immediate(offset / elementSize) : null;
    }

    // value = Multiplier * Root + Offset (a null Root means it's just a constant)
    private readonly record struct Affine(LocalVariable? Root, long Multiplier, long Offset);

    private static Affine? Evaluate(IOperand operand, Dictionary<LocalVariable, Instruction?> definitions, int depth)
    {
        if (depth > 8)
            return null;

        switch (operand)
        {
            case Immediate { Value: var value }:
                return new Affine(null, 0, value);

            case LocalVariable local:
            {
                if (!definitions.TryGetValue(local, out var definition) || definition == null)
                    return new Affine(local, 1, 0);

                return definition switch
                {
                    { OpCode: OpCode.Move, Operands: [_, MemoryOperand lea] } => EvaluateLea(lea, definitions, depth + 1),
                    { OpCode: OpCode.Move, Operands: [_, var source] } => Evaluate(source, definitions, depth + 1),
                    { OpCode: OpCode.Add, Operands: [_, var left, var right] } => Sum(Evaluate(left, definitions, depth + 1), Evaluate(right, definitions, depth + 1)),
                    { OpCode: OpCode.ShiftLeft, Operands: [_, var left, Immediate { Value: >= 0 and < 32 } shift] } => ScaleBy(Evaluate(left, definitions, depth + 1), 1L << (int)shift.Value),
                    { OpCode: OpCode.Multiply, Operands: [_, var left, Immediate factor] } => ScaleBy(Evaluate(left, definitions, depth + 1), factor.Value),
                    _ => new Affine(local, 1, 0)
                };
            }

            default:
                return null;
        }
    }

    private static Affine? EvaluateLea(MemoryOperand lea, Dictionary<LocalVariable, Instruction?> definitions, int depth)
    {
        var result = (Affine?)new Affine(null, 0, lea.Addend);

        if (lea.Base != null)
            result = Sum(result, Evaluate(lea.Base, definitions, depth));

        if (lea.Index != null)
            result = Sum(result, ScaleBy(Evaluate(lea.Index, definitions, depth), Math.Max(lea.Scale, 1)));

        return result;
    }

    private static Affine? Sum(Affine? left, Affine? right)
    {
        if (left is not { } l || right is not { } r)
            return null;

        if (l.Root != null && r.Root != null && !ReferenceEquals(l.Root, r.Root))
            return null;

        return new Affine(l.Root ?? r.Root, l.Multiplier + r.Multiplier, l.Offset + r.Offset);
    }

    private static Affine? ScaleBy(Affine? value, long factor)
        => value is { } affine ? new Affine(affine.Root, affine.Multiplier * factor, affine.Offset * factor) : null;

    // null means the local has more than one definition
    private static Dictionary<LocalVariable, Instruction?> SingleDefinitions(ISILControlFlowGraph cfg)
    {
        var definitions = new Dictionary<LocalVariable, Instruction?>();

        foreach (var instruction in cfg.Instructions)
            if (instruction.Destination is LocalVariable destination)
                definitions[destination] = definitions.ContainsKey(destination) ? null : instruction;

        return definitions;
    }

    private static Dictionary<LocalVariable, List<(Instruction Instruction, int OperandIndex)>> CollectUses(ISILControlFlowGraph cfg)
    {
        var uses = new Dictionary<LocalVariable, List<(Instruction, int)>>();

        foreach (var instruction in cfg.Instructions)
        {
            for (var i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (ReferenceEquals(operand, instruction.Destination))
                    continue;

                foreach (var local in OperandLocals(operand))
                {
                    if (!uses.TryGetValue(local, out var sites))
                        uses[local] = sites = [];
                    sites.Add((instruction, i));
                }
            }
        }

        return uses;
    }

    private static IEnumerable<LocalVariable> OperandLocals(IOperand operand)
    {
        switch (operand)
        {
            case LocalVariable direct:
                yield return direct;
                break;
            case MemoryOperand memory:
                if (memory.Base is LocalVariable baseLocal)
                    yield return baseLocal;
                if (memory.Index is LocalVariable indexLocal)
                    yield return indexLocal;
                break;
            case AddressOf { Target: LocalVariable addressed }:
                yield return addressed;
                break;
        }
    }
}
