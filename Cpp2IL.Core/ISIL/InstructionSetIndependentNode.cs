using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class InstructionSetIndependentNode
{
    public List<IsilStatement> Statements = new();

    public IsilBuilder GetBuilder() => new(Statements);
}