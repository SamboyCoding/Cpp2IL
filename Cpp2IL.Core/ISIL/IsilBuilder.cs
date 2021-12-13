using System;
using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.ISIL;

public class IsilBuilder
{
    public List<IsilStatement> BackingStatementList;

    public IsilBuilder(List<IsilStatement> backingStatementList)
    {
        BackingStatementList = backingStatementList;
    }

    private void AddInstruction(InstructionSetIndependentInstruction instruction) => BackingStatementList.Add(new IsilInstructionStatement(instruction));
    
    public void AppendIf(IsilIfStatement ifStatement) => BackingStatementList.Add(ifStatement);
    
    public void Move(InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Move, dest, src));
    
    public void LoadAddress(InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.LoadAddress, dest, src));

    public void ShiftStack(int amount) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftStack, InstructionSetIndependentOperand.MakeImmediate(amount)));
    
    public void Push(InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Move, InstructionSetIndependentOperand.MakeStack(0), operand));
    public void Pop(InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Move, operand, InstructionSetIndependentOperand.MakeStack(0)));
    
    public void Subtract(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Subtract, left, right));
    public void Add(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Add, left, right));
    public void Xor(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Xor, left, right));
    public void Call(ulong dest, params InstructionSetIndependentOperand[] args) => AddInstruction(new(InstructionSetIndependentOpCode.Call, new [] {InstructionSetIndependentOperand.MakeImmediate(dest)}.Concat(args).ToArray()));
    
    public void Return(InstructionSetIndependentOperand? returnValue = null) => AddInstruction(new(InstructionSetIndependentOpCode.Return, returnValue != null ? new []{returnValue} : Array.Empty<InstructionSetIndependentOperand>()));
}