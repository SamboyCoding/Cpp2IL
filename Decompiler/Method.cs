using AsmResolver.DotNet;
using Decompiler.IL;

namespace Decompiler;

/// <summary>
/// A method definition.
/// </summary>
public class Method(MethodDefinition definition, List<Instruction> instructions)
{
    /// <summary>
    /// The method definition.
    /// </summary>
    public MethodDefinition Definition = definition;

    /// <summary>
    /// All instructions.
    /// </summary>
    public List<Instruction> Instructions = instructions;

    public override string ToString() => Definition.Name!;
}
