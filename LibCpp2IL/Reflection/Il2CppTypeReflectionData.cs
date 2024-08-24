using System.Text;
using LibCpp2IL.Metadata;

#pragma warning disable 8618
namespace LibCpp2IL.Reflection;

/// <summary>
/// A wrapper around Il2CppTypeDefinition to allow expression of complex types such as Generics. Can represent one of three things:
/// <ul>
/// <li>If both <see cref="isType"/> and <see cref="isGenericType"/> are false, this represents a generic parameter (such as the T in List&lt;T>)</li>
/// <li>If <see cref="isType"/> is true and <see cref="isGenericType"/> is false, this represents a standard Il2CppTypeDefinition - check <see cref="baseType"/> to see what</li>
/// <li>If both <see cref="isType"/> and <see cref="isGenericType"/> are true, this is a complex generic type, of basic type <see cref="baseType"/> - this is where the List part would be - and with params stored in <see cref="genericParams"/> - these may be any of these three cases again.</li>
/// </ul>
/// <br/>
/// Calling <see cref="ToString"/> on this object will return the canonical representation of this object, with generic params such as System.Collections.Generic.List`1&lt;T> or with concrete types, like in the case of a String's interfaces, <code>System.Collections.Generic.IEnumerable`1&lt;System.Char&gt;</code>
/// </summary>
public class Il2CppTypeReflectionData
{
    public Il2CppTypeDefinition? baseType;
    public Il2CppTypeReflectionData[] genericParams;
    public bool isType;
    public bool isGenericType;
    public bool isArray;
    public Il2CppTypeReflectionData? arrayType;
    public byte arrayRank;
    public string variableGenericParamName;
    public long variableGenericParamIndex;
    public bool isPointer;
#pragma warning restore 8618

    private string GetPtrSuffix()
    {
        return isPointer ? "*" : "";
    }

    public override string ToString()
    {
        if (isArray)
            return arrayType + "[]".Repeat(arrayRank) + GetPtrSuffix();

        if (!isType)
            return variableGenericParamName + GetPtrSuffix();

        if (!isGenericType)
            return baseType!.FullName! + GetPtrSuffix();

        var builder = new StringBuilder(baseType!.FullName + "<");
        foreach (var genericParam in genericParams)
        {
            builder.Append(genericParam).Append(", ");
        }

        builder.Remove(builder.Length - 2, 2);
        builder.Append(">");
        return builder + GetPtrSuffix();
    }
}
