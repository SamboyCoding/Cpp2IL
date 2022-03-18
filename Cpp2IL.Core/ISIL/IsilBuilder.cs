using System;
using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class IsilBuilder
{
    public List<InstructionSetIndependentInstruction> BackingStatementList;

    public IsilBuilder()
    {
        BackingStatementList = new();
    }
    
    public IsilBuilder(List<InstructionSetIndependentInstruction> backingStatementList)
    {
        BackingStatementList = backingStatementList;
    }

    private void AddInstruction(InstructionSetIndependentInstruction instruction) => BackingStatementList.Add(instruction);

    public void Move(InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.Move, dest, src));

    public void LoadAddress(InstructionSetIndependentOperand dest, InstructionSetIndependentOperand src) => AddInstruction(new(InstructionSetIndependentOpCode.LoadAddress, dest, src));

    public void ShiftStack(int amount) => AddInstruction(new(InstructionSetIndependentOpCode.ShiftStack, InstructionSetIndependentOperand.MakeImmediate(amount)));

    public void Push(InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Move, InstructionSetIndependentOperand.MakeStack(0), operand));
    public void Pop(InstructionSetIndependentOperand operand) => AddInstruction(new(InstructionSetIndependentOpCode.Move, operand, InstructionSetIndependentOperand.MakeStack(0)));

    public void Subtract(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Subtract, left, right));
    public void Add(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Add, left, right));
    public void Xor(InstructionSetIndependentOperand left, InstructionSetIndependentOperand right) => AddInstruction(new(InstructionSetIndependentOpCode.Xor, left, right));
    public void Call(ulong dest, params InstructionSetIndependentOperand[] args) => AddInstruction(new(InstructionSetIndependentOpCode.Call, PrepareCallOperands(dest, args)));

    public void Return(InstructionSetIndependentOperand? returnValue = null) => AddInstruction(new(InstructionSetIndependentOpCode.Return, returnValue != null ? new[] {returnValue.Value} : Array.Empty<InstructionSetIndependentOperand>()));

    private InstructionSetIndependentOperand[] PrepareCallOperands(ulong dest, InstructionSetIndependentOperand[] args)
    {
        var parameters = new InstructionSetIndependentOperand[args.Length + 1];
        parameters[0] = InstructionSetIndependentOperand.MakeImmediate(dest);
        Array.Copy(args, 0, parameters, 1, args.Length);
        return parameters;
    }
}