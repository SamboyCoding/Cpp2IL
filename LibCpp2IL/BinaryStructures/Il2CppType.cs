using System;
using System.Reflection;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppType : ReadableClass
{
    public ulong Datapoint;
    public uint Bits;
    public Union Data { get; set; } = null!; //Late-bound
    public uint Attrs { get; set; }
    public Il2CppTypeEnum Type { get; set; }
    public uint NumMods { get; set; }
    public uint Byref { get; set; }
    public uint Pinned { get; set; }
    public uint ValueType { get; set; }

    private void InitUnionAndFlags()
    {
        Attrs = Bits & 0b1111_1111_1111_1111; //Lowest 16 bits
        Type = (Il2CppTypeEnum)((Bits >> 16) & 0b1111_1111); //Bits 16-23
        Data = new Union { Dummy = Datapoint };

        if (LibCpp2IlMain.Il2CppTypeHasNumMods5Bits)
        {
            //Unity 2021 (v27.2) changed num_mods to be 5 bits not 6
            //Which shifts byref and pinned left one
            //And adds a new bit 31 which is valuetype
            NumMods = (Bits >> 24) & 0b1_1111;
            Byref = (Bits >> 29) & 1;
            Pinned = (Bits >> 30) & 1;
            ValueType = Bits >> 31;
        }
        else
        {
            NumMods = (Bits >> 24) & 0b11_1111;
            Byref = (Bits >> 30) & 1;
            Pinned = Bits >> 31;
            ValueType = 0;
        }
    }

    public class Union
    {
        public ulong Dummy;
        public long ClassIndex => (long)Dummy;
        public ulong Type => Dummy;
        public ulong Array => Dummy;
        public long GenericParameterIndex => (long)Dummy;
        public ulong GenericClass => Dummy;
    }

    public Il2CppTypeDefinition AsClass()
    {
        if (Type is not Il2CppTypeEnum.IL2CPP_TYPE_CLASS and not Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
            throw new Exception("Type is not a class, but a " + Type);

        return LibCpp2IlMain.TheMetadata!.typeDefs[Data.ClassIndex];
    }

    public Il2CppType GetEncapsulatedType()
    {
        if (Type is not Il2CppTypeEnum.IL2CPP_TYPE_PTR and not Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY)
            throw new Exception("Type does not have a encapsulated type - it is not a pointer or an szarray");

        return LibCpp2IlMain.Binary!.GetIl2CppTypeFromPointer(Data.Type);
    }

    public Il2CppArrayType GetArrayType()
    {
        if (Type is not Il2CppTypeEnum.IL2CPP_TYPE_ARRAY)
            throw new Exception("Type is not an array");

        return LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppArrayType>(Data.Array);
    }

    public Il2CppType GetArrayElementType() => LibCpp2IlMain.Binary!.GetIl2CppTypeFromPointer(GetArrayType().etype);

    public int GetArrayRank() => GetArrayType().rank;

    public Il2CppGenericParameter GetGenericParameterDef()
    {
        if (Type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            throw new Exception("Type is not a generic parameter");

        return LibCpp2IlMain.TheMetadata!.genericParameters[Data.GenericParameterIndex];
    }

    public Il2CppGenericClass GetGenericClass()
    {
        if (Type is not Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            throw new Exception("Type is not a generic class");

        return LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppGenericClass>(Data.GenericClass);
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        Datapoint = reader.ReadNUint();
        Bits = reader.ReadUInt32();

        InitUnionAndFlags();
    }

    public Il2CppTypeDefinition CoerceToUnderlyingTypeDefinition()
    {
        if (Type is Il2CppTypeEnum.IL2CPP_TYPE_VAR or Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
            throw new("Can't get the type definition of a generic parameter");

        return Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST => GetGenericClass().TypeDefinition,
            Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => GetEncapsulatedType().CoerceToUnderlyingTypeDefinition(),
            Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => GetArrayElementType().CoerceToUnderlyingTypeDefinition(),
            _ => Type.IsIl2CppPrimitive() ? LibCpp2IlReflection.PrimitiveTypeDefinitions[Type] : AsClass()
        };
    }

    public bool ThisOrElementIsGenericParam()
    {
        return Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_PTR or Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => GetEncapsulatedType().ThisOrElementIsGenericParam(),
            Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => GetArrayElementType().ThisOrElementIsGenericParam(),
            Il2CppTypeEnum.IL2CPP_TYPE_MVAR or Il2CppTypeEnum.IL2CPP_TYPE_VAR => true,
            _ => false
        };
    }

    public string GetGenericParamName()
    {
        if (!ThisOrElementIsGenericParam())
            throw new("Type is not a generic parameter");

        return Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_PTR => $"{GetEncapsulatedType().GetGenericParamName()}&",
            Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => $"{GetEncapsulatedType().GetGenericParamName()}[]",
            Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => $"{GetArrayElementType().GetGenericParamName()}{"[]".Repeat(GetArrayRank())}",
            _ => $"{GetGenericParameterDef().Name}",
        };
    }
}
