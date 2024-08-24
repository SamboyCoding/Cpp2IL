using System;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppPropertyDefinition : ReadableClass, IIl2CppTokenProvider
{
    public int nameIndex;
    public int get;
    public int set;
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

    public Il2CppMethodDefinition? Getter => LibCpp2IlMain.TheMetadata == null || get < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.FirstMethodIdx + get];

    public Il2CppMethodDefinition? Setter => LibCpp2IlMain.TheMetadata == null || set < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.FirstMethodIdx + set];

    public Il2CppTypeReflectionData? PropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter == null ? Setter!.Parameters![0].Type : Getter!.ReturnType;

    public Il2CppType? RawPropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter == null ? Setter!.Parameters![0].RawType : Getter!.RawReturnType;

    public bool IsStatic => Getter == null ? Setter!.IsStatic : Getter!.IsStatic;
    public uint Token => token;

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();

        //Cache name now
        var pos = reader.Position;
        Name = ((Il2CppMetadata)reader).ReadStringFromIndexNoReadLock(nameIndex);
        reader.Position = pos;

        get = reader.ReadInt32();
        set = reader.ReadInt32();
        attrs = reader.ReadUInt32();

        if (IsAtMost(24f))
            customAttributeIndex = reader.ReadInt32();

        token = reader.ReadUInt32();
    }
}
