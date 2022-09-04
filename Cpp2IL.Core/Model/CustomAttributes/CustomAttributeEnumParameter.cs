using System.IO;
using System.Linq;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;

public class CustomAttributeEnumParameter : BaseCustomAttributeParameter
{
    public readonly Il2CppType EnumType;
    public readonly Il2CppType UnderlyingPrimitiveType;
    public readonly CustomAttributePrimitiveParameter UnderlyingPrimitiveParameter;

    public CustomAttributeEnumParameter(Il2CppType enumType, ApplicationAnalysisContext context, AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
        EnumType = enumType;
        var enumTypeDef = EnumType.AsClass();
        UnderlyingPrimitiveType = enumTypeDef.EnumUnderlyingType;
        UnderlyingPrimitiveParameter = new(UnderlyingPrimitiveType.Type, owner, kind, index);
    }

    public Il2CppTypeEnum GetTypeByte() => UnderlyingPrimitiveType.Type;
    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context) => UnderlyingPrimitiveParameter.ReadFromV29Blob(reader, context);

    public override string ToString()
    {
        var enumTypeDef = EnumType.AsClass();
        var matchingField = enumTypeDef.Fields?.FirstOrDefault(f => Equals(f.DefaultValue?.Value, UnderlyingPrimitiveParameter.PrimitiveValue));
        
        if(matchingField != null)
            return $"{enumTypeDef.Name}::{matchingField.Name} ({UnderlyingPrimitiveParameter.PrimitiveValue})";
        
        return UnderlyingPrimitiveParameter.ToString();
    }
}
