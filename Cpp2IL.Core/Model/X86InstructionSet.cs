using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.Model;

public class X86InstructionSet : BaseInstructionSet
{
    public override IControlFlowGraph BuildGraphForMethod(MethodAnalysisContext context)
    {
        if (context.UnderlyingPointer == 0)
            return new X86ControlFlowGraph(new(), context.AppContext.Binary.is32Bit, context.AppContext.GetOrCreateKeyFunctionAddresses());
        
        List<Instruction> instructions;

        if (context is not AttributeGeneratorMethodAnalysisContext)
            instructions = X86Utils.GetManagedMethodBody(context.Definition!).ToList();
        else
        {
            var rawMethodBody = GetRawBytesForMethod(context, context is AttributeGeneratorMethodAnalysisContext);
            instructions = X86Utils.Disassemble(rawMethodBody, context.UnderlyingPointer).ToList();
        }

        return new X86ControlFlowGraph(instructions, context.AppContext.Binary.is32Bit, context.AppContext.GetOrCreateKeyFunctionAddresses());
    }

    public override byte[] GetRawBytesForMethod(MethodAnalysisContext context, bool isAttributeGenerator) => X86Utils.GetRawManagedOrCaCacheGenMethodBody(context.UnderlyingPointer, isAttributeGenerator);

    public override BaseKeyFunctionAddresses CreateKeyFunctionAddressesInstance() => new X86KeyFunctionAddresses();

    public override List<InstructionSetIndependentNode> ControlFlowGraphToISIL(IControlFlowGraph graph, MethodAnalysisContext context)
    {
        var ret = new List<InstructionSetIndependentNode>();

        graph.TraverseEntireGraphPreOrder(node =>
        {
            if (node is not X86ControlFlowGraphNode x86Node)
                throw new("How did we get a non-x86 node?");

            var isilNode = new InstructionSetIndependentNode
            {
                Statements = new((int) (x86Node.Statements.Count * 1.2f)) //Allocate initial capacity of 20% larger than source
            };
            
            var builder = isilNode.GetBuilder();
            foreach (var x86NodeStatement in x86Node.Statements)
            {
                ConvertStatement(x86NodeStatement, builder, context);
            }

            ret.Add(isilNode);
        });

        return ret;
    }

    private void ConvertStatement(IStatement statement, IsilBuilder builder, MethodAnalysisContext context)
    {
        if (statement is IfStatement<Instruction> ifStatement)
        {
            ConvertIfStatement(ifStatement, builder, context);
            return;
        }

        if (statement is not InstructionStatement<Instruction> instructionStatement)
            throw new("How did we get a non-instruction, non-if statement?");

        ConvertInstructionStatement(instructionStatement, builder, context);
    }

    private void ConvertIfStatement(IfStatement<Instruction> ifStatement, IsilBuilder builder, MethodAnalysisContext context)
    {
        var conditionLeft = ConvertOperand(ifStatement.Condition.Comparison, 0);
        var conditionRight = ConvertOperand(ifStatement.Condition.Comparison, 1);
        var comparisonOpcode = ifStatement.Condition.Jump.Mnemonic switch
        {
            Mnemonic.Jge => InstructionSetIndependentOpCode.CompareGreaterThanOrEqual,
            Mnemonic.Jg => InstructionSetIndependentOpCode.CompareGreaterThan,
            Mnemonic.Jle => InstructionSetIndependentOpCode.CompareLessThanOrEqual,
            Mnemonic.Jl => InstructionSetIndependentOpCode.CompareLessThan,
            Mnemonic.Jne => InstructionSetIndependentOpCode.CompareNotEqual,
            Mnemonic.Je => InstructionSetIndependentOpCode.CompareEqual,
            _ => throw new("Unknown comparison opcode"),
        };

        var condition = new IsilCondition(conditionLeft, conditionRight, comparisonOpcode);
        if (ifStatement.Condition.Comparison.Mnemonic == Mnemonic.Test)
            condition = condition.MarkAsAnd();

        var ret = new IsilIfStatement(condition);

        var ifBuilder = ret.GetIfBuilder();
        foreach (var statement in ifStatement.IfBlock) 
            ConvertStatement(statement, ifBuilder, context);
        
        var elseBuilder = ret.GetElseBuilder();
        foreach (var statement in ifStatement.ElseBlock) 
            ConvertStatement(statement, elseBuilder, context);

        builder.AppendIf(ret);
    }

    private void ConvertInstructionStatement(InstructionStatement<Instruction> instructionStatement, IsilBuilder builder, MethodAnalysisContext context)
    {
        var instruction = instructionStatement.Instruction;

        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov:
                builder.Move(ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Lea:
                builder.LoadAddress(ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Xor:
                builder.Xor(ConvertOperand(instruction, 0), ConvertOperand(instruction, 1));
                break;
            case Mnemonic.Ret:
                if(context.IsVoid)
                    builder.Return();
                else
                    builder.Return(InstructionSetIndependentOperand.MakeRegister("rax")); //TODO Support xmm0
                break;
            case Mnemonic.Push:
            {
                var operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.ShiftStack(-operandSize);
                builder.Push(ConvertOperand(instruction, 0));
                break;
            }
            case Mnemonic.Pop:
            {
                var operandSize = instruction.Op0Kind == OpKind.Register ? instruction.Op0Register.GetSize() : instruction.MemorySize.GetSize();
                builder.Pop(ConvertOperand(instruction, 0));
                builder.ShiftStack(operandSize);
                break;
            }
            case Mnemonic.Sub:
            case Mnemonic.Add:
                var isSubtract = instruction.Mnemonic == Mnemonic.Sub;
                
                //Special case - stack shift
                if (instruction.Op0Register == Register.RSP && instruction.Op1Kind.IsImmediate())
                {
                    var amount = (int) instruction.GetImmediate(1);
                    builder.ShiftStack(isSubtract ? -amount : amount);
                    break;
                }

                var left = ConvertOperand(instruction, 0);
                var right = ConvertOperand(instruction, 1);
                if(isSubtract)
                    builder.Subtract(left, right);
                else
                    builder.Add(left, right);
                
                break;
            case Mnemonic.Call:
                //We don't try and resolve which method is being called, but we do need to know how many parameters it has
                //I would hope that all of these methods have the same number of arguments, else how can they be inlined?
                var target = instruction.NearBranchTarget;
                if (context.AppContext.MethodsByAddress.ContainsKey(target))
                {
                    var possibleMethods = context.AppContext.MethodsByAddress[target];
                    var parameterCounts = possibleMethods.Select(p =>
                    {
                        var ret = p.Parameters.Length;
                        if (!p.IsStatic)
                            ret++; //This arg
                        
                        ret++; //For MethodInfo arg
                        return ret;
                    }).ToArray();

                    // if (parameterCounts.Max() != parameterCounts.Min())
                        // throw new("Cannot handle call to address with multiple managed methods of different parameter counts");
                    
                    var parameterCount = parameterCounts.Max();
                    var registerParams = new[] {"rcx", "rdx", "r8", "r9"}.Select(InstructionSetIndependentOperand.MakeRegister).ToList();
                    
                    if(context.AppContext.Binary.is32Bit)
                        registerParams.Clear(); //Nothing pushed in registers on 32-bit
                    
                    if (parameterCount <= registerParams.Count)
                    {
                        builder.Call(target, registerParams.GetRange(0, parameterCount).ToArray());
                        break;
                    }
                    
                    //Need to use stack
                    parameterCount -= registerParams.Count; //Subtract the 4 params we can fit in registers

                    //Generate and append stack operands
                    var ptrSize = context.AppContext.Binary.is32Bit ? 4 : 8;
                    registerParams = registerParams.Concat(Enumerable.Range(0, parameterCount).Select(p => p * ptrSize).Select(InstructionSetIndependentOperand.MakeStack)).ToList();
                    
                    builder.Call(target, registerParams.ToArray());
                    
                    //Discard the consumed stack space
                    builder.ShiftStack(-parameterCount * 8);
                }
                else
                {
                    //This isn't a managed method, so for now we don't know its parameter count.
                    //Add all four of the registers, I guess
                    //TODO Store data on number of parameters each KFA takes, and use that. Also, 32-bit doesn't use these registers
                    var paramRegisters = new[] {"rcx", "rdx", "r8", "r9"}.Select(InstructionSetIndependentOperand.MakeRegister).ToArray();
                    builder.Call(target, paramRegisters);
                }
                break;

        }
    }

    private InstructionSetIndependentOperand ConvertOperand(Instruction instruction, int operand)
    {
        var kind = instruction.GetOpKind(operand);

        if (kind == OpKind.Register)
            return InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.GetOpRegister(operand)));
        if (kind.IsImmediate())
            return InstructionSetIndependentOperand.MakeImmediate(instruction.GetImmediate(operand));
        if (kind == OpKind.Memory && instruction.MemoryBase == Register.RSP)
            return InstructionSetIndependentOperand.MakeStack((int) instruction.MemoryDisplacement32);

        //Memory
        //Most complex to least complex
        
        if(instruction.IsIPRelativeMemoryOperand)
            return InstructionSetIndependentOperand.MakeMemory(new(instruction.IPRelativeMemoryAddress));

        //All four components
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 != 0)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryIndex));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, mIndex, instruction.MemoryDisplacement32, instruction.MemoryIndexScale));
        }

        //No addend
        if (instruction.MemoryIndex != Register.None && instruction.MemoryBase != Register.None)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            var mIndex = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryIndex));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, mIndex, instruction.MemoryIndexScale));
        }

        //No index (and so no scale)
        if (instruction.MemoryBase != Register.None && instruction.MemoryDisplacement64 > 0)
        {
            var mBase = InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase));
            return InstructionSetIndependentOperand.MakeMemory(new(mBase, instruction.MemoryDisplacement64));
        }

        //Only base
        if (instruction.MemoryBase != Register.None)
        {
            return InstructionSetIndependentOperand.MakeMemory(new(InstructionSetIndependentOperand.MakeRegister(X86Utils.GetRegisterName(instruction.MemoryBase))));
        }

        //Only addend
        return InstructionSetIndependentOperand.MakeMemory(new(instruction.MemoryDisplacement64));
    }
}