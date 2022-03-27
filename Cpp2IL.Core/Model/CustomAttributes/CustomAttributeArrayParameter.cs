using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;


/// <summary>
/// Represents a custom attribute parameter which is an array of other parameters, which can themselves potentially be arrays, enums, etc.
///
/// When parsing this, first check IsNullArray - if it's true, emit a null and continue.
///
/// Then check EnumType - if it's non-null, it means this is an enum array, not a simple primitive array, and the type of the enum should be used when emitting.
///
/// If it's null, use ArrType to determine the type of the array, which will be a primitive, string, or type.
///
/// Then read the ArrayElements list and output each one. Remember to type-prefix if ArrType is Object.
/// </summary>
public class CustomAttributeArrayParameter : BaseCustomAttributeParameter
{
    public bool IsNullArray;
    public Il2CppType? EnumType;
    public Il2CppTypeEnum ArrType;
    public List<BaseCustomAttributeParameter> ArrayElements;

    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var arrLength = reader.BaseStream.ReadUnityCompressedInt();
        if (arrLength == -1)
        {
            //Array length is -1 when the array itself is null
            IsNullArray = true;
            return;
        }

        ArrType = (Il2CppTypeEnum) reader.ReadByte();
        if (ArrType == Il2CppTypeEnum.IL2CPP_TYPE_ENUM)
        {
            var enumTypeIndex = reader.BaseStream.ReadUnityCompressedInt();
            
            //Save the actual enum type for later.
            EnumType = context.Binary.GetType(enumTypeIndex);
            
            //We read as the primitive underlying type.
            var enumClass = EnumType.AsClass();
            ArrType = enumClass.EnumUnderlyingType.type;
        }

        var arrayElementsAreTypePrefixed = reader.ReadBoolean();
        
        if(arrayElementsAreTypePrefixed && ArrType != Il2CppTypeEnum.IL2CPP_TYPE_OBJECT)
            throw new("Array elements are type-prefixed, but the array type is not object");
        
        ArrayElements = new();
        
        for (var i = 0; i < arrLength; i++)
        {
            var thisType = ArrType;
            
            if(arrayElementsAreTypePrefixed)
                thisType = (Il2CppTypeEnum) reader.ReadByte();

            //ConstructParameterForType will handle reading the enum type
            var arrayElement = V29AttributeUtils.ConstructParameterForType(reader, context, thisType);
            
            arrayElement.ReadFromV29Blob(reader, context);
            
            ArrayElements.Add(arrayElement);
        }
    }

    public override string ToString()
    {
        if (IsNullArray)
            return "(array) null";
        
        var arrType = ArrType.ToString();
        if (EnumType != null)
            arrType = EnumType.AsClass().ToString();
        
        return $"new {arrType}[] {{{string.Join(", ", ArrayElements.Select(x => x.ToString()))}}}]";
    }
}