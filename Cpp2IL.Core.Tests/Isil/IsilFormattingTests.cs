namespace Cpp2IL.Core.Tests.Isil;

using Cpp2IL.Core.ISIL;

public class IsilFormattingTests
{
	[Test]
	public void ToString_FormatsJumpWithHexTarget()
	{
		var instruction = new Instruction(12, OpCode.Jump, (ulong)0x1Au);

		Assert.That(instruction.ToString(), Is.EqualTo("12 Jump 001A"));
	}

	[Test]
	public void ToString_FormatsConditionalJumpWithHexTargetAndCondition()
	{
		var condition = new Register(null, "ZF");
		var instruction = new Instruction(5, OpCode.ConditionalJump, (ulong)0x2Bu, condition);

		Assert.That(instruction.ToString(), Is.EqualTo("5 ConditionalJump 002B, ZF"));
	}

	[TestCase(OpCode.CallVoid)]
	[TestCase(OpCode.Call)]
	public void ToString_FormatsCallLikeOpCodesWithHexTargetAndRemainingOperands(OpCode opcode)
	{
		var arg0 = new Register(null, "rcx");
		var instruction = new Instruction(42, opcode, (ulong)0x1234u, arg0, "hello");

		Assert.That(instruction.ToString(), Is.EqualTo($"42 {opcode} 1234, rcx, \"hello\""));
	}

	[Test]
	public void ToString_UsesDefaultPathForNonSpecialOpcode()
	{
		var destination = new Register(null, "rax");
		var source = new Register(null, "rbx");
		var instruction = new Instruction(3, OpCode.Move, destination, source);

		Assert.That(instruction.ToString(), Is.EqualTo("3 Move rax, rbx"));
	}

	[Test]
	public void ToString_ReturnWithoutOperands_DoesNotHaveTrailingSpace()
	{
		var instruction = new Instruction(17, OpCode.Return);

		Assert.That(instruction.ToString(), Is.EqualTo("17 Return"));
	}
}
