using System.IO;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a <see cref="BaseCustomAttributeTypeParameter"/> for a <see cref="Il2CppType"/>.
/// </summary>
public class CustomAttributeTypeParameter : BaseCustomAttributeTypeParameter
{
    private Il2CppType? _type;
    private TypeAnalysisContext? _typeContext;

    public override TypeAnalysisContext? TypeContext
    {
        get
        {
            return _typeContext ??= Owner.Constructor.CustomAttributeAssembly.ResolveIl2CppType(_type);
        }
    }

    public CustomAttributeTypeParameter(Il2CppType? type, AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
        _type = type;
    }

    public CustomAttributeTypeParameter(TypeAnalysisContext? type, AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
        _typeContext = type;
    }

    public CustomAttributeTypeParameter(AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
    }

    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        var typeIndex = reader.BaseStream.ReadUnityCompressedInt();
        if (typeIndex == -1)
            _type = null;
        else
        {
            _type = context.Binary.GetType(typeIndex);
        }
        _typeContext = null;
    }

    public override string ToString()
    {
        if (TypeContext == null)
            return "(Type) null";

        if (TypeContext.IsPrimitive)
            return $"typeof({LibCpp2ILUtils.GetTypeName(TypeContext.Type)}";

        if (TypeContext is ReferencedTypeAnalysisContext)
        {
            return $"typeof({TypeContext.GetCSharpSourceString()})";
        }

        //Basic class/struct
        return $"typeof({TypeContext.Name})";
    }
}
