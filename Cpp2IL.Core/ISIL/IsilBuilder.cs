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
        BackingStatementList = [];
        InstructionAddressMap = new();
        _jumpsToFix = [];
    }

    public IsilBuilder(List<InstructionSetIndependentInstruction> backingStatementList)
    {
        BackingStatementList = backingStatementList;
        InstructionAddressMap = new();
        _jumpsToFix = [];
    }

    private void AddInstruction(InstructionSetIndependentInstruction instruction)
    {
        if (InstructionAddressMap.TryGetValue(instruction.ActualAddress, out var list))
        {
            list.Add(instruction);
        }
        else
        {
            InstructionAddressMap[instruction.ActualAddress] = [instruction];
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

                if (target.Equals(tuple.Item1))
                    tuple.Item1.Invalidate("Invalid jump target for instruction: Instruction can't jump to itself");
                else
                    tuple.Item1.Operands = [InstructionSetIndependentOperand.MakeInstruction(target)];
            }
            else
            {
                tuple.Item1.Invalidate("Jump target not found in method.");
            }
        }
    }

    public void Move(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Move, instructionAddress, IsilFlowControl.Continue, dest, src));

    public void LoadAddress(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.LoadAddress, instructionAddress, IsilFlowControl.Continue, dest, src));

    public void ShiftStack(ulong instructionAddress, int amount) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftStack, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(amount)));

    public void Push(ulong instructionAddress, InstructionSetIndependentOperand stackPointerRegister, InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Push, instructionAddress, IsilFlowControl.Continue, stackPointerRegister, operand));
    public void Pop(ulong instructionAddress, InstructionSetIndependentOperand stackPointerRegister, InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Pop, instructionAddress, IsilFlowControl.Continue, operand, stackPointerRegister));

    public void Exchange(ulong instructionAddress, InstructionSetIndependentOperand place1, InstructionSetIndependentOperand place2) => AddInstruction(new(InstructionSetIndependentOpCode.Exchange, instructionAddress, IsilFlowControl.Continue, place1, place2));

    public void Subtract(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Subtract, instructionAddress, IsilFlowControl.Continue, dest, left, right));
    public void Add(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Add, instructionAddress, IsilFlowControl.Continue, dest, left, right));

    public void Xor(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Xor, instructionAddress, IsilFlowControl.Continue, dest, left, right));

    // The following 4 had their opcode implemented but not the builder func
    // I don't know why
    public void ShiftLeft(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftLeft, instructionAddress, IsilFlowControl.Continue, left, right));
    public void ShiftRight(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftRight, instructionAddress, IsilFlowControl.Continue, left, right));
    public void And(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.And, instructionAddress, IsilFlowControl.Continue, dest, left, right));
    public void Or(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Or, instructionAddress, IsilFlowControl.Continue, dest, left, right));

    public void Not(ulong instructionAddress, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Not, instructionAddress, IsilFlowControl.Continue, src));
    public void Multiply(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src1, InstructionSetIndependentOperand src2) => AddInstruction(new(InstructionSetIndependentOpCode.Multiply, instructionAddress, IsilFlowControl.Continue, dest, src1, src2));

    public void Call(ulong instructionAddress, ulong dest, params InstructionSetIndependentOperand[] args) => AddInstruction(new(InstructionSetIndependentOpCode.Call, instructionAddress, IsilFlowControl.MethodCall, PrepareCallOperands(dest, args)));

    public void CallRegister(ulong instructionAddress, InstructionSetIndependentOperand dest, bool noReturn = false) => AddInstruction(new(noReturn ? InstructionSetIndependentOpCode.CallNoReturn : InstructionSetIndependentOpCode.Call, instructionAddress, IsilFlowControl.MethodCall, dest));

    public void Return(ulong instructionAddress, InstructionSetIndependentOperand? returnValue = null) => AddInstruction(new(InstructionSetIndependentOpCode.Return, instructionAddress, IsilFlowControl.MethodReturn, returnValue != null
        ?
        [
            returnValue.Value
        ]
        : []));

    public void Goto(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.Goto, IsilFlowControl.UnconditionalJump);

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


    public void Compare(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Compare, instructionAddress, IsilFlowControl.Continue, left, right));

    public void NotImplemented(ulong instructionAddress, string text) => AddInstruction(new(InstructionSetIndependentOpCode.NotImplemented, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(text)));

    public void Invalid(ulong instructionAddress, string text) => AddInstruction(new(InstructionSetIndependentOpCode.Invalid, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(text)));

    public void Interrupt(ulong instructionAddress) => AddInstruction(new(InstructionSetIndependentOpCode.Interrupt, instructionAddress, IsilFlowControl.Interrupt));
    public void Nop(ulong instructionAddress) => AddInstruction(new(InstructionSetIndependentOpCode.Nop, instructionAddress, IsilFlowControl.Continue));

    private InstructionSetIndependentOperand[] PrepareCallOperands(ulong dest, InstructionSetIndependentOperand[] args)
    {
        var parameters = new InstructionSetIndependentOperand[args.Length + 1];
        parameters[0] = InstructionSetIndependentOperand.MakeImmediate(dest);
        Array.Copy(args, 0, parameters, 1, args.Length);
        return parameters;
    }
}
