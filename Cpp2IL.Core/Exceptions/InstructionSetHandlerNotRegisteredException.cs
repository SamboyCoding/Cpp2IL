using System;
using LibCpp2IL;

namespace Cpp2IL.Core.Exceptions;

public class InstructionSetHandlerNotRegisteredException : Exception
{
    public InstructionSetHandlerNotRegisteredException(InstructionSetId instructionSetId) 
        : base($"Instruction set handler not registered for instruction set {instructionSetId.Name}. Please register an instruction set handler using InstructionSetRegistry.RegisterInstructionSet<T>(id)")
    {
    }
}