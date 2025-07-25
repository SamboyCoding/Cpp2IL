using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public class LocalVariable(string name, Register register, TypeAnalysisContext? type = null)
{
    public string Name = name;
    public Register Register = register;

    /// <summary>
    /// null if typeprop has not been done yet, or if the type could not be determined.
    /// </summary>
    public TypeAnalysisContext? Type = type;

    public bool IsThis = false;
    public bool IsReturn = false;
    public bool IsMethodInfo = false;

    public override string ToString() => Type == null ? $"{Name} @ {Register}" : $"{Name} @ {Register} ({Type.FullName})";
}
