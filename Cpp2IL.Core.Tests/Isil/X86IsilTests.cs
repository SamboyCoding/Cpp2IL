using Cpp2IL.Core.ISIL;
using System.Collections.Generic;

namespace Cpp2IL.Core.Tests.Isil;

public class X86IsilTests
{
    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        TestGameLoader.LoadSimple2019Game();
    }

    [Test]
    public void X86IsilConversionTestSimpleIf()
    {
        var appContext = Cpp2IlApi.CurrentAppContext!;
        var mscorlib = appContext.AssembliesByName["mscorlib"];
        var appDomain = mscorlib.GetTypeByFullName("System.AppDomain");

        Assert.That(appDomain, Is.Not.Null, "expected to find System.AppDomain in mscorlib");

        var method = appDomain!.GetMethod("DoDomainUnload");
        var isil = appContext.InstructionSet.GetIsilFromMethod(method);

        Assert.That(isil, Is.Not.Null.And.Not.Empty, "expected ISIL conversion to produce instructions");

        var rax = new Register(null, "rax");
        var rcx = new Register(null, "rcx");
        var rdx = new Register(null, "rdx");
        var r8 = new Register(null, "r8");
        var r9 = new Register(null, "r9");
        var CF = new Register(null, "CF");
        var OF = new Register(null, "OF");
        var SF = new Register(null, "SF");
        var ZF = new Register(null, "ZF");
        var PF = new Register(null, "PF");
        var TEMP1 = new Register(null, "TEMP1");
        var TEMP2 = new Register(null, "TEMP2");
        var TEMP3 = new Register(null, "TEMP3");
        var TEMP4 = new Register(null, "TEMP4");
        var TEMP5 = new Register(null, "TEMP5");

        var instructions = new List<Instruction>();

        void Add(int index, OpCode opCode, params object[] operands) =>
            instructions.Add(new Instruction(index, opCode, operands));
        
        Add(0, OpCode.Move, rax, new MemoryOperand(rcx, null, 0x48));
        Add(1, OpCode.CheckLess, CF, rax, 0);
        Add(2, OpCode.Subtract, TEMP1, rax, 0);
        Add(3, OpCode.Xor, TEMP2, rax, 0);
        Add(4, OpCode.Xor, TEMP3, rax, TEMP1);
        Add(5, OpCode.And, TEMP4, TEMP2, TEMP3);
        Add(6, OpCode.CheckLess, OF, TEMP4, 0);
        Add(7, OpCode.CheckLess, SF, TEMP1, 0);
        Add(8, OpCode.CheckEqual, ZF, TEMP1, 0);
        Add(9, OpCode.And, TEMP5, TEMP2, 1);
        Add(10, OpCode.CheckEqual, PF, TEMP5, 0);
        Add(11, OpCode.ConditionalJump, 18, ZF);
        Add(12, OpCode.Move, rdx, rcx);
        Add(13, OpCode.Move, r9, 0);
        Add(14, OpCode.Move, rcx, rax);
        Add(15, OpCode.Move, r8, 0);
        Add(16, OpCode.CallVoid, (ulong)0x180267A70, rcx, rdx, r8, r9);
        Add(17, OpCode.Return);
        Add(18, OpCode.Return);
        
        Assert.That(isil.Count == instructions.Count,
            $"expected instruction count to be {instructions.Count}, but got {isil.Count}");

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode is OpCode.Jump or OpCode.ConditionalJump)
                instruction.Operands[0] = instructions[(int)instruction.Operands[0]];

            Assert.True(instruction.IsStructurallyEqualTo(isil[i]), $"expected: {instruction}, but got {isil[i]}");
        }
    }
}
    
