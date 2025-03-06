using AsmResolver.DotNet;
using Decompiler.IL;

namespace Decompiler;

/// <summary>
/// A method definition.
/// </summary>
public class Method(MethodDefinition definition, List<Instruction>? instructions, List<IOperand> parameters)
{
    /// <summary>
    /// The method definition.
    /// </summary>
    public MethodDefinition Definition = definition;

    /// <summary>
    /// All instructions.
    /// </summary>
    public List<Instruction> Instructions = instructions ?? [];

    /// <summary>
    /// Parameter locations.
    /// </summary>
    public List<IOperand> Parameters = parameters;

    public override string ToString() => Definition.Name!;
}
