using System;

namespace Cpp2IL.Core.Exceptions;

public class NodeConditionCalculationException : Exception
{
    public NodeConditionCalculationException(string message) : base(message)
    {
    }
}