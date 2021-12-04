using System.Collections.Generic;

namespace Cpp2IL.Core.Graphs;

public struct InstructionGraphUseDef
{
    public List<string> Uses;
    public List<string> Definitions;

    public InstructionGraphUseDef()
    {
        Uses = new();
        Definitions = new();
    }
}