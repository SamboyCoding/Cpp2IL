using System.IO;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;

public class CustomAttributeEnumParameter : BaseCustomAttributeParameter
{
    private readonly Il2CppType _enumType;
    private readonly Il2CppType _underlyingPrimitiveType;
    private readonly CustomAttributePrimitiveParameter _underlyingPrimitiveParameter;

    public CustomAttributeEnumParameter(Il2CppType enumType, ApplicationAnalysisContext context)
    {
        _enumType = enumType;
        var enumTypeDef = _enumType.AsClass();
        _underlyingPrimitiveType = context.Binary.GetType(enumTypeDef.elementTypeIndex);
        _underlyingPrimitiveParameter = new CustomAttributePrimitiveParameter(_underlyingPrimitiveType.type);
    }

    public Il2CppTypeEnum GetTypeByte() => _underlyingPrimitiveType.type;
    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context) => _underlyingPrimitiveParameter.ReadFromV29Blob(reader, context);

    public override string ToString()
    {
        var enumTypeDef = _enumType.AsClass();
        var matchingField = enumTypeDef.Fields?.FirstOrDefault(f => Equals(f.DefaultValue?.Value, _underlyingPrimitiveParameter.PrimitiveValue));
        
        if(matchingField != null)
            return $"{enumTypeDef.Name}::{matchingField.Name} ({_underlyingPrimitiveParameter.PrimitiveValue})";
        
        return _underlyingPrimitiveParameter.ToString();
    }
}