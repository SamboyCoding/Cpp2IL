using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.InstructionSets;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
            instructions.Add(new Instruction(index, opCode, Ops(operands)));
        
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
                instruction.SetOperand(0, instructions[(int)((Immediate)instruction.Operands[0]).Value]);

            Assert.True(instruction.IsStructurallyEqualTo(isil[i]), $"expected: {instruction}, but got {isil[i]}");
        }
    }
    
    [Test]
    public void X86IsilLeaInstructionTests()
    {
        Register Register(string name) => new(null, name);

        var testCases = new (string Bytes, ulong Ip, Instruction[] Expected)[]
        {
            ("8d 03", 0, [new(0, OpCode.Move, Register("rax"), Register("rbx"))]), // lea eax, [rbx]
            ("8d 43 01", 0, [new(0, OpCode.Add, Register("rax"), Register("rbx"), Imm(1L))]), // lea eax, [rbx+0x1]
            ("8d 4b ff", 0, [new(0, OpCode.Subtract, Register("rcx"), Register("rbx"), Imm(1L))]), // lea ecx, [rbx-0x1]
            ("48 8d 0c 24", 0, [new(0, OpCode.Move, Register("rcx"), new AddressOf(new StackOffset(0)))]), // lea rcx, [rsp]
            ("48 8d 4c 24 30", 0, [new(0, OpCode.Move, Register("rcx"), new AddressOf(new StackOffset(0x30)))]), // lea rcx, [rsp+0x30]
            ("48 8d 4c 24 f0", 0, [new(0, OpCode.Move, Register("rcx"), new AddressOf(new StackOffset(-0x10)))]), // lea rcx, [rsp-0x10]
            ("48 8d 04 0b", 0, [new(0, OpCode.Add, Register("rax"), Register("rbx"), Register("rcx"))]), // lea rax, [rbx+rcx]
            ("48 8d 44 8b 10", 0, [new(0, OpCode.Multiply, Register("TEMP"), Register("rcx"), Imm(4)), 
                                   new(1, OpCode.Add, Register("rax"), Register("rbx"), Register("TEMP")), 
                                   new(2, OpCode.Add, Register("rax"), Register("rax"), Imm(0x10L))]), // lea rax, [rbx+rcx*4+0x10]
            ("48 8d 04 8d 78 56 34 12", 0, [new(0, OpCode.Multiply, Register("rax"), Register("rcx"), Imm(4)), 
                                            new(1, OpCode.Add, Register("rax"), Register("rax"), Imm(0x12345678L))]), // lea rax, [rcx*4+0x12345678]
            ("48 8d 04 25 78 56 34 12", 0, [new(0, OpCode.Move, Register("rax"), Imm(0x12345678L))]), // lea rax, [0x12345678]
            ("48 8d 83 00 00 00 80", 0, [new(0, OpCode.Subtract, Register("rax"), Register("rbx"), Imm(0x80000000L))]), // lea rax, [rbx-0x80000000]
            ("48 8d 45 00", 0, [new(0, OpCode.Move, Register("rax"), Register("rbp"))]), // lea rax, [rbp]
            ("48 8d 04 80", 0, [new(0, OpCode.Multiply, Register("TEMP"), Register("rax"), Imm(4)), 
                                new(1, OpCode.Add, Register("rax"), Register("rax"), Register("TEMP"))]), // lea rax, [rax+rax*4]
            ("48 8d 0d 10 4b d1 01", 0x1803A2C51UL, [new(0, OpCode.Move, Register("rcx"), Imm(0x1820B7768L))]), // lea rcx, [rel 0x1820b7768]
        };
        foreach (var (bytes, ip, expected) in testCases)
        {
            var instructionBytes = bytes.Split(' ').Select(byteValue => byte.Parse(byteValue, NumberStyles.HexNumber));
            var decoder = Iced.Intel.Decoder.Create(64, new Iced.Intel.ByteArrayCodeReader(instructionBytes.ToArray()));
            decoder.IP = ip;
            decoder.Decode(out var x86Instruction);

            var isil = new X86InstructionSet().GetIsilFromInstruction(x86Instruction);
            
            Assert.That(isil.Count, Is.EqualTo(expected.Length), x86Instruction.ToString);
            for (var i = 0; i < expected.Length; i++)
                Assert.That(isil[i].IsStructurallyEqualTo(expected[i]), Is.True, x86Instruction.ToString);
        }
    }
}
    
