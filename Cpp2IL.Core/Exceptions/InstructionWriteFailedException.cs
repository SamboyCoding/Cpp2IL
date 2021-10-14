using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Exceptions
{
    public class InstructionWriteFailedException : Exception
    {
        public InstructionWriteFailedException(Instruction insn, Exception cause) : base($"Failed to write operand for instruction {insn} due to an exception", cause)
        { }
    }
}