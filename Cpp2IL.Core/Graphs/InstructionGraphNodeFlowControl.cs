namespace Cpp2IL.Core;

public enum InstructionGraphNodeFlowControl
{
    Continue,
    UnconditionalJump,
    ConditionalJump,
    IndirectJump,
    Call,
    IndirectCall,
    Return,
    NoReturn,
}