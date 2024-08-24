using System;
using System.Linq;
using System.Reflection;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppEventDefinition : ReadableClass
{
    public int nameIndex;
    public int typeIndex;
    public int add;
    public int remove;
    public int raise;
    [Version(Max = 24)] public int customAttributeIndex; //Not in 24.1 or 24.2
    public uint token;

    [NonSerialized] private Il2CppTypeDefinition? _type;

    public Il2CppTypeDefinition? DeclaringType
    {
        get
        {
            if (_type != null) return _type;
            if (LibCpp2IlMain.TheMetadata == null) return null;

            _type = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(t => t.Events!.Contains(this));
            return _type;
        }
        internal set => _type = value;
    }

    public string? Name { get; private set; }

    public Il2CppType? RawType => LibCpp2IlMain.Binary?.GetType(typeIndex);

    public Il2CppTypeReflectionData? EventType => LibCpp2IlMain.Binary == null ? null : LibCpp2ILUtils.GetTypeReflectionData(RawType!);

    public EventAttributes EventAttributes => (EventAttributes)RawType!.Attrs;

    public Il2CppMethodDefinition? Adder => LibCpp2IlMain.TheMetadata == null || add < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.FirstMethodIdx + add];

    public Il2CppMethodDefinition? Remover => LibCpp2IlMain.TheMetadata == null || remove < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.FirstMethodIdx + remove];

    public Il2CppMethodDefinition? Invoker => LibCpp2IlMain.TheMetadata == null || raise < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.FirstMethodIdx + raise];

    public bool IsStatic
    {
        get
        {
            if (Adder != null)
                return Adder.IsStatic;
            if (Remover != null)
                return Remover.IsStatic;

            return Invoker!.IsStatic;
        }
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();

        //Cache name now
        var pos = reader.Position;
        Name = ((Il2CppMetadata)reader).ReadStringFromIndexNoReadLock(nameIndex);
        reader.Position = pos;

        typeIndex = reader.ReadInt32();
        add = reader.ReadInt32();
        remove = reader.ReadInt32();
        raise = reader.ReadInt32();
        if (IsAtMost(24f))
            customAttributeIndex = reader.ReadInt32();
        token = reader.ReadUInt32();
    }
}
