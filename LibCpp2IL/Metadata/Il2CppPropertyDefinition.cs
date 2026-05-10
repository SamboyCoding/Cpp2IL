using System;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppPropertyDefinition : ReadableClass, IIl2CppTokenProvider
{
    public int nameIndex;
    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> get;
    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> set;
    public uint attrs;
    [Version(Max = 24)] public int customAttributeIndex;
    public uint token;

    [NonSerialized] private Il2CppTypeDefinition? _type;

    public int PropertyIndex => LibCpp2IlReflection.GetPropertyIndexFromProperty(this);

    public Il2CppTypeDefinition? DeclaringType
    {
        get
        {
            if (_type != null)
                return _type;

            if (LibCpp2IlMain.TheMetadata == null) return null;

            _type = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(t => t.Properties!.Contains(this));
            return _type;
        }
        internal set => _type = value;
    }

    public string? Name { get; private set; }

    public Il2CppMethodDefinition? Getter => LibCpp2IlMain.TheMetadata == null || get.IsNull || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.GetMethodDefinitionFromIndex(DeclaringType.FirstMethodIdx + get);

    public Il2CppMethodDefinition? Setter => LibCpp2IlMain.TheMetadata == null || set.IsNull || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.GetMethodDefinitionFromIndex(DeclaringType.FirstMethodIdx + set);

    public Il2CppTypeReflectionData? PropertyType
    {
        get
        {
            if (LibCpp2IlMain.TheMetadata == null) return null;
            if (Getter != null) return Getter.ReturnType;
            if (Setter != null && Setter.Parameters is { Length: > 0 }) return Setter.Parameters[^1].Type;
            return null;
        }
    }

    public Il2CppType? RawPropertyType
    {
        get
        {
            if (LibCpp2IlMain.TheMetadata == null) return null;
            if (Getter != null) return Getter.RawReturnType;
            if (Setter != null && Setter.Parameters is { Length: > 0 }) return Setter.Parameters[^1].RawType;
            return null;
        }
    }

    public bool IsStatic => Getter?.IsStatic ?? Setter?.IsStatic ?? false;
    public uint Token => token;

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();

        //Cache name now
        var pos = reader.Position;
        Name = ((Il2CppMetadata)reader).ReadStringFromIndexNoReadLock(nameIndex);
        reader.Position = pos;

        get = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
        set = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
        attrs = reader.ReadUInt32();

        if (IsAtMost(24f))
            customAttributeIndex = reader.ReadInt32();

        token = reader.ReadUInt32();
    }
}
