using AsmResolver.DotNet.Signatures;

namespace Decompiler.IL;

/// <summary>
/// IL local variable.
/// </summary>
public class LocalVariable(string name, Register register, TypeSignature? type = null)
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
    public TypeSignature? Type = type;

    /// <summary>
    /// Is this the 'this' parameter?
    /// </summary>
    public bool IsThis = false;

    public override string ToString() => Type == null ? Name : $"{Name}:{Type.Name}";
}
