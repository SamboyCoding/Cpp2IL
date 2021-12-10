using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

public class IsilIfStatement
{
    public IsilCondition Condition;
    public List<IsilStatement> IfBlock = new();
    public List<IsilStatement> ElseBlock = new();

    public IsilIfStatement(IsilCondition condition)
    {
        Condition = condition;
    }
}