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
    private Il2CppType? _type;
    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var typeIndex = reader.BaseStream.ReadUnityCompressedInt();
        if (typeIndex == -1)
            _type = null;
        else
        {
            _type = context.Binary.GetType(typeIndex);
        }
    }

    public override string ToString()
    {
        if(_type == null)
            return "(Type) null";
        
        return $"typeof({_type.AsClass().Name})";
    }
}