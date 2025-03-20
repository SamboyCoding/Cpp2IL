using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace Cpp2IL.Decompiler.IL;

/// <summary>
/// IL local variable.
/// </summary>
public class LocalVariable(string name, Register register, TypeDefinition? type = null)
{
    /// <summary>
    /// Name of the variable.
    /// </summary>
    public string Name = name;

    /// <summary>
    /// Location of the variable.
    /// </summary>
    public Register Register = register;

    /// <summary>
    /// Type of the variable.
    /// </summary>
    public TypeDefinition? Type = type;

    /// <summary>
    /// The field that this local accesses.
    /// </summary>
    public FieldDefinition? Field;

    /// <summary>
    /// Is this the 'this' parameter?
    /// </summary>
    public bool IsThis = false;

    public override string ToString() => Type == null ? Name : (Field == null ? $"{Name}:{Type.Name}" : $"{Name}:{Type.Name}.{Field.Name}");
}
