namespace Cpp2IL.Core.Graphs;

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
    Entry,
    Exit,
}