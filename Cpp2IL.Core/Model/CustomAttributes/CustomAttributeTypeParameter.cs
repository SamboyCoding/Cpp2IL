using System.IO;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils.AsmResolver;
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
        
        return $"typeof({Type.AsClass().Name})";
    }

    public override TypeSignature? ToTypeSignature(ModuleDefinition parentModule)
    {
        return Type == null ? null : AsmResolverUtils.GetTypeSignatureFromIl2CppType(parentModule, Type);
    }
}
