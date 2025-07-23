using System;
using System.Text;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public class LocalVariable(string name, Register register, TypeAnalysisContext? type = null)
{
    public string Name = name;
    public Register Register = register;

    /// <summary>
    /// null if typeprop has not been done yet.
    /// </summary>
    public TypeAnalysisContext? Type = type;

    public bool IsThis = false;
    public bool IsReturn = false;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Name);
        if (Type != null)
            sb.Append($" ({Type.Name})");
        sb.Append($" ({Register})");
        return sb.ToString();
    }
}
