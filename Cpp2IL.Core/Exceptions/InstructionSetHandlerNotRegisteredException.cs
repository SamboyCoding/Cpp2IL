using System;
using LibCpp2IL;

namespace Cpp2IL.Core.Exceptions;

public class InstructionSetHandlerNotRegisteredException(InstructionSetId instructionSetId) : Exception(
    $"Instruction set handler not registered for instruction set {instructionSetId.Name}. Please register an instruction set handler using InstructionSetRegistry.RegisterInstructionSet<T>(id)");
