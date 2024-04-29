using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Exceptions;

namespace Cpp2IL.Core.ISIL;

public class IsilBuilder
{
    public List<InstructionSetIndependentInstruction> BackingStatementList;

    public Dictionary<ulong, List<InstructionSetIndependentInstruction>> InstructionAddressMap;

    // (Goto instruction, target instruction address)
    private readonly List<(InstructionSetIndependentInstruction, ulong)> _jumpsToFix;

    public IsilBuilder()
    {
        BackingStatementList = new();
        InstructionAddressMap = new();
        _jumpsToFix = new();
    }

    public IsilBuilder(List<InstructionSetIndependentInstruction> backingStatementList)
    {
        BackingStatementList = backingStatementList;
        InstructionAddressMap = new();
        _jumpsToFix = new();
    }

    private void AddInstruction(InstructionSetIndependentInstruction instruction)
    {
        if (InstructionAddressMap.ContainsKey(instruction.ActualAddress))
        {
            InstructionAddressMap[instruction.ActualAddress].Add(instruction);
        }
        else
        {
            var newList = new List<InstructionSetIndependentInstruction>();
            newList.Add(instruction);
            InstructionAddressMap[instruction.ActualAddress] = newList;
        }
        BackingStatementList.Add(instruction);
        instruction.InstructionIndex = (uint)BackingStatementList.Count;
    }

    public void FixJumps()
    {
        foreach (var tuple in _jumpsToFix)
        {
            if (InstructionAddressMap.TryGetValue(tuple.Item2, out var list))
            {
                var target = list.First();
                if (target is null)
                    throw new IsilConversionException("This can't ever happen");

                if (target.Equals(tuple.Item1))
                    throw new IsilConversionException("Invalid jump target for instruction: Instruction can't jump to itself");

                tuple.Item1.Operands = new[] { InstructionSetIndependentOperand.MakeInstruction(target) };
            }
            else
            {
                throw new IsilConversionException("Jump target not found in method. Ruh roh");
            }
        }
    }

    public void Move(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Move, instructionAddress, IsilFlowControl.Continue, dest, src));

    public void LoadAddress(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.LoadAddress, instructionAddress, IsilFlowControl.Continue, dest, src));

    public void ShiftStack(ulong instructionAddress, int amount) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftStack, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(amount)));

    public void Push(ulong instructionAddress, InstructionSetIndependentOperand stackPointerRegister, InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Push, instructionAddress, IsilFlowControl.Continue, stackPointerRegister, operand));
    public void Pop(ulong instructionAddress, InstructionSetIndependentOperand stackPointerRegister, InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Pop, instructionAddress, IsilFlowControl.Continue, operand, stackPointerRegister));

    public void Convert(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src, (Type src, Type dest) typeInfo) => AddInstruction(new(InstructionSetIndependentOpCode.Convert, instructionAddress, IsilFlowControl.Continue, dest, src, InstructionSetIndependentOperand.MakeInfo(typeInfo)));

    public void Shuffle(ulong instructionAddress, InstructionSetIndependentOperand arg1, InstructionSetIndependentOperand arg2, InstructionSetIndependentOperand arg3) => AddInstruction(new(InstructionSetIndependentOpCode.Shuffle, instructionAddress, IsilFlowControl.Continue, arg1, arg2, arg3));
    public void Shuffle(ulong instructionAddress, InstructionSetIndependentOperand arg1, InstructionSetIndependentOperand arg2, InstructionSetIndependentOperand arg3, InstructionSetIndependentOperand arg4) => AddInstruction(new(InstructionSetIndependentOpCode.LongShuffle, instructionAddress, IsilFlowControl.Continue, arg1, arg2, arg3, arg4));

    public void Exchange(ulong instructionAddress, InstructionSetIndependentOperand place1, InstructionSetIndependentOperand place2) => AddInstruction(new(InstructionSetIndependentOpCode.Exchange, instructionAddress, IsilFlowControl.Continue, place1, place2));

    public void Subtract(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Subtract, instructionAddress, IsilFlowControl.Continue, left, right));
    public void Add(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Add, instructionAddress, IsilFlowControl.Continue, left, right));
    public void Xor(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Xor, instructionAddress, IsilFlowControl.Continue, left, right));
    public void Neg(ulong instructionAddress, InstructionSetIndependentOperand value) => AddInstruction(new(InstructionSetIndependentOpCode.Neg, instructionAddress, IsilFlowControl.Continue, value));
    // The following 4 had their opcode implemented but not the builder func
    // I don't know why
    public void ShiftLeft(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftLeft, instructionAddress, IsilFlowControl.Continue, left, right));
    public void ShiftRight(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftRight, instructionAddress, IsilFlowControl.Continue, left, right));
    public void And(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.And, instructionAddress, IsilFlowControl.Continue, left, right));
    public void Or(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Or, instructionAddress, IsilFlowControl.Continue, left, right));

    public void RotateLeft(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.RotateLeft, instructionAddress, IsilFlowControl.Continue, left, right));
    public void RotateRight(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.RotateRight, instructionAddress, IsilFlowControl.Continue, left, right));

    public void Not(ulong instructionAddress, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Not, instructionAddress, IsilFlowControl.Continue, src));
    public void Multiply(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src1, InstructionSetIndependentOperand src2) => AddInstruction(new(InstructionSetIndependentOpCode.Multiply, instructionAddress, IsilFlowControl.Continue, dest, src1, src2));

    public void Divide(ulong instructionAddress, InstructionSetIndependentOperand dest) => AddInstruction(new(InstructionSetIndependentOpCode.Divide1, instructionAddress, IsilFlowControl.Continue, dest));
    public void Divide(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Divide2, instructionAddress, IsilFlowControl.Continue, left, right));
    public void Divide(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src, InstructionSetIndependentOperand div) => AddInstruction(new(InstructionSetIndependentOpCode.Divide3, instructionAddress, IsilFlowControl.Continue, dest, src, div));

    public void BitTest(ulong instructionAddress, BitTestType type, InstructionSetIndependentOperand src, InstructionSetIndependentOperand bitOffset) => AddInstruction(new(InstructionSetIndependentOpCode.BitTest, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeInfo(type), src, bitOffset));

    public void Call(ulong instructionAddress, ulong dest, params InstructionSetIndependentOperand[] args) => AddInstruction(new(InstructionSetIndependentOpCode.Call, instructionAddress, IsilFlowControl.MethodCall, PrepareCallOperands(dest, args)));

    public void Return(ulong instructionAddress, InstructionSetIndependentOperand? returnValue = null) => AddInstruction(new(InstructionSetIndependentOpCode.Return, instructionAddress, IsilFlowControl.MethodReturn, returnValue != null ? new[] { returnValue.Value } : Array.Empty<InstructionSetIndependentOperand>()));

    public void Goto(ulong instructionAddress, InstructionSetIndependentOperand register) => AddInstruction(new(InstructionSetIndependentOpCode.GotoRegister, instructionAddress, IsilFlowControl.UnconditionalJump));

    public void Goto(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.Goto, IsilFlowControl.UnconditionalJump);

    public void JumpIfSign(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfNotSign, IsilFlowControl.ConditionalJump);

    public void JumpIfNotSign(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfSign, IsilFlowControl.ConditionalJump);

    public void JumpIfParity(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfParity, IsilFlowControl.ConditionalJump);

    public void JumpIfEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfEqual, IsilFlowControl.ConditionalJump);

    public void JumpIfNotEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfNotEqual, IsilFlowControl.ConditionalJump);

    public void JumpIfGreater(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfGreater, IsilFlowControl.ConditionalJump);

    public void JumpIfLess(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfLess, IsilFlowControl.ConditionalJump);

    public void JumpIfGreaterOrEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfGreaterOrEqual, IsilFlowControl.ConditionalJump);

    public void JumpIfLessOrEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfLessOrEqual, IsilFlowControl.ConditionalJump);

    private void CreateJump(ulong instructionAddress, ulong target, InstructionSetIndependentOpCode independentOpCode, IsilFlowControl flowControl)
    {
        var newInstruction = new InstructionSetIndependentInstruction(
            independentOpCode,
            instructionAddress,
            flowControl
        );
        AddInstruction(newInstruction);
        _jumpsToFix.Add((newInstruction, target));
    }

    public void SignExtend(ulong instructionAddress, InstructionSetIndependentOperand reg) => AddInstruction(new(InstructionSetIndependentOpCode.SignExtend, instructionAddress, IsilFlowControl.Continue, reg));
    public void ZeroExtend(ulong instructionAddress, InstructionSetIndependentOperand reg) => AddInstruction(new(InstructionSetIndependentOpCode.ZeroExtend, instructionAddress, IsilFlowControl.Continue, reg));

    public void ByteSwap(ulong instructionAddress, InstructionSetIndependentOperand reg) => AddInstruction(new(InstructionSetIndependentOpCode.ByteSwap, instructionAddress, IsilFlowControl.Continue, reg));
    public void CursedCPUFlags(ulong instructionAddress, InstructionSetIndependentOperand reg, string whyCursed) => AddInstruction(new(InstructionSetIndependentOpCode.CursedCPUFlags, instructionAddress, IsilFlowControl.Continue, reg, InstructionSetIndependentOperand.MakeInfo(whyCursed)));

    public void Nop(ulong instructionAddress) => AddInstruction(new(InstructionSetIndependentOpCode.Nop, instructionAddress, IsilFlowControl.Continue));

    public void Compare(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Compare, instructionAddress, IsilFlowControl.Continue, left, right));

    public void NotImplemented(ulong instructionAddress, string text) => AddInstruction(new(InstructionSetIndependentOpCode.NotImplemented, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(text)));

    public void Interrupt(ulong instructionAddress) => AddInstruction(new(InstructionSetIndependentOpCode.Interrupt, instructionAddress, IsilFlowControl.Interrupt));

    private InstructionSetIndependentOperand[] PrepareCallOperands(ulong dest, InstructionSetIndependentOperand[] args)
    {
        var parameters = new InstructionSetIndependentOperand[args.Length + 1];
        parameters[0] = InstructionSetIndependentOperand.MakeImmediate(dest);
        Array.Copy(args, 0, parameters, 1, args.Length);
        return parameters;
    }


}
