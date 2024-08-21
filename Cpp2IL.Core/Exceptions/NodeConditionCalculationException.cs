using System;

namespace Cpp2IL.Core.Exceptions;

public class NodeConditionCalculationException(string message) : Exception(message);