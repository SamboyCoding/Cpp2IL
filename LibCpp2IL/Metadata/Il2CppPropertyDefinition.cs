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

    public int PropertyIndex => OwningContext.ReflectionCache.GetPropertyIndexFromProperty(this);

    public Il2CppTypeDefinition? DeclaringType
    {
        get
        {
            if (_type != null)
                return _type;

            _type = OwningContext.Metadata.typeDefs.FirstOrDefault(t => t.Properties!.Contains(this));
            return _type;
        }
        internal set => _type = value;
    }

    public string? Name { get; private set; }

    public Il2CppMethodDefinition? Getter => get.IsNull || DeclaringType == null ? null : OwningContext.Metadata.GetMethodDefinitionFromIndex(DeclaringType.FirstMethodIdx + get);

    public Il2CppMethodDefinition? Setter => set.IsNull || DeclaringType == null ? null : OwningContext.Metadata.GetMethodDefinitionFromIndex(DeclaringType.FirstMethodIdx + set);

    public Il2CppTypeReflectionData? PropertyType => Getter == null ? Setter!.Parameters![0].Type : Getter!.ReturnType;

    public Il2CppType? RawPropertyType => Getter == null ? Setter!.Parameters![0].RawType : Getter!.RawReturnType;

    public bool IsStatic => Getter == null ? Setter!.IsStatic : Getter!.IsStatic;
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
