namespace Cpp2IL.Core.Graphs;

public enum BlockType : byte
{
    OneWay, // etc. Jumps to another block
    TwoWay, // etc. Jumps conditionally to two blocks
    NWay, // switch statement nonsense I think
    Call, // Block finishes with call
    Return, // Block finishes with return

    // we fall into next block, for example block A has
    // mov reg1, reg2
    // mov reg2, reg3
    //
    // block b has 
    // mov reg3, reg4
    // mov reg2, reg4
    // and another block finishes with a jump to start instruction of block b meaning block a falls into b (bad explanation)
    Fall,

    // Block type is not known yet
    Unknown,

    // Exception or something raised
    Interrupt,

    // Empty blocks that serve as entry and exit markers
    Entry,
    Exit,
}
