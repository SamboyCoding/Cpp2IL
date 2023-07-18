using System.IO;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a <see cref="BaseCustomAttributeTypeParameter"/> for a <see cref="Il2CppType"/>.
/// </summary>
public class CustomAttributeTypeParameter : BaseCustomAttributeTypeParameter
{
    public Il2CppType? Type;

    public CustomAttributeTypeParameter(Il2CppType? type, AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
        Type = type;
    }

    public CustomAttributeTypeParameter(AnalyzedCustomAttribute owner, CustomAttributeParameterKind kind, int index) : base(owner, kind, index)
    {
    }

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

        if (Type.Type.IsIl2CppPrimitive())
            return $"typeof({LibCpp2ILUtils.GetTypeName(Owner.Constructor.AppContext.Metadata, Owner.Constructor.AppContext.Binary, Type)}";

        if (Type.Type is not Il2CppTypeEnum.IL2CPP_TYPE_CLASS and not Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
        {
            //Some sort of wrapper type, like a generic parameter or a generic instance.
            var typeContext = Owner.Constructor.CustomAttributeAssembly.ResolveIl2CppType(Type);
            return $"typeof({typeContext.GetCSharpSourceString()})";
        }
        
        //Basic class/struct
        return $"typeof({Type.AsClass().Name})";
    }

    public override TypeSignature? ToTypeSignature(ModuleDefinition parentModule)
    {
        return Type == null ? null : AsmResolverUtils.GetTypeSignatureFromIl2CppType(parentModule, Type);
    }
}
