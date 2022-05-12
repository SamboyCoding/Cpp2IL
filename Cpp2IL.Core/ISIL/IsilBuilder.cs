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
                tuple.Item1.Operands = new[] {InstructionSetIndependentOperand.MakeInstruction(target)};
            }
            else
            {
                throw new IsilConversionException("Jump target not found in method. Ruh roh");
            }
        }
    }

    public void Move(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Move, instructionAddress, IsilFlowControl.Continue, dest, src));

    public void LoadAddress(ulong instructionAddress, InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.LoadAddress, instructionAddress,IsilFlowControl.Continue,  dest, src));

    public void ShiftStack(ulong instructionAddress, int amount) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftStack,  instructionAddress,IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(amount)));

    public void Push(ulong instructionAddress, InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Move,  instructionAddress,IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeStack(0), operand));
    public void Pop(ulong instructionAddress, InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Move, instructionAddress, IsilFlowControl.Continue, operand, InstructionSetIndependentOperand.MakeStack(0)));

    public void Subtract(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Subtract,  instructionAddress, IsilFlowControl.Continue, left, right));
    public void Add(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Add, instructionAddress,IsilFlowControl.Continue,  left, right));
    public void Xor(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Xor, instructionAddress,IsilFlowControl.Continue,  left, right));

    public void Call(ulong instructionAddress, ulong dest, params InstructionSetIndependentOperand[] args) => AddInstruction(new(InstructionSetIndependentOpCode.Call, instructionAddress, IsilFlowControl.MethodCall, PrepareCallOperands(dest, args)));

    public void Return(ulong instructionAddress, InstructionSetIndependentOperand? returnValue = null) => AddInstruction(new(InstructionSetIndependentOpCode.Return,  instructionAddress, IsilFlowControl.MethodReturn, returnValue != null ? new[] {returnValue.Value} : Array.Empty<InstructionSetIndependentOperand>()));

    public void Goto(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.Goto, IsilFlowControl.UnconditionalJump);

    public void JumpIfEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfEqual, IsilFlowControl.ConditionalJump);

    public void JumpIfNotEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfNotEqual, IsilFlowControl.ConditionalJump);
    
    public void JumpIfGreater(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfGreater, IsilFlowControl.ConditionalJump);

    public void JumpIfLess(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfLess, IsilFlowControl.ConditionalJump);

    public void JumpIfGreaterOrEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfGreaterOrEqual, IsilFlowControl.ConditionalJump);
    
    public void JumpIfLessOrEqual(ulong instructionAddress, ulong target) => CreateJump(instructionAddress, target, InstructionSetIndependentOpCode.JumpIfLessOrEqual, IsilFlowControl.ConditionalJump);

    private void CreateJump(ulong instructionAddress, ulong target, InstructionSetIndependentOpCode independentOpCode, IsilFlowControl flowControl)
    {
        var newInstruction =  new InstructionSetIndependentInstruction(
            independentOpCode,
            instructionAddress,
            flowControl
        );
        AddInstruction(newInstruction);
        _jumpsToFix.Add((newInstruction, target));
    }
    

    public void Compare(ulong instructionAddress, InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Compare, instructionAddress, IsilFlowControl.Continue, left, right));

    public void NotImplemented(ulong instructionAddress, string text) => AddInstruction(new (InstructionSetIndependentOpCode.NotImplemented, instructionAddress, IsilFlowControl.Continue, InstructionSetIndependentOperand.MakeImmediate(text)));
    
    public void Interrupt(ulong instructionAddress) => AddInstruction(new (InstructionSetIndependentOpCode.Interrupt, instructionAddress, IsilFlowControl.Interrupt));

    private InstructionSetIndependentOperand[] PrepareCallOperands(ulong dest, InstructionSetIndependentOperand[] args)
    {
        var parameters = new InstructionSetIndependentOperand[args.Length + 1];
        parameters[0] = InstructionSetIndependentOperand.MakeImmediate(dest);
        Array.Copy(args, 0, parameters, 1, args.Length);
        return parameters;
    }
    
    
}