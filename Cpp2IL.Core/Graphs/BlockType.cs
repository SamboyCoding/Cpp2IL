namespace Cpp2IL.Core.Graphs;

public enum BlockType : byte
{
    OneWay, // etc. Jumps to another block
    TwoWay, // etc. Jumps conditionally to two blocks
    NWay, // switch statement nonsense I think
    Call, // Block finishes with call
    TailCall, // Block finishes with tail call, clears stack, and returns
    Return, // Block finishes with return
    Fall, // Falls to the next block, like if the block has more than one predecessor and this is one of those

    // Block type is not known yet
    Unknown,

    // Exception or something raised
    Interrupt,

    // Empty blocks that serve as entry and exit markers
    Entry,
    Exit,
}
