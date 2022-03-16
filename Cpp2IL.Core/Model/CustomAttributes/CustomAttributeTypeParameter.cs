using System.IO;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a custom attribute parameter which is a type reference (typeof(x))
/// </summary>
public class CustomAttributeTypeParameter : BaseCustomAttributeParameter
{
    public Il2CppType? Type;
    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var typeIndex = reader.BaseStream.ReadUnityCompressedInt();
        if (typeIndex == -1)
            Type = null;
        else
        {
            Type = context.Binary.GetType(typeIndex);
        }
    }

    public override string ToString()
    {
        if(Type == null)
            return "(Type) null";
        
        return $"typeof({Type.AsClass().Name})";
    }
}