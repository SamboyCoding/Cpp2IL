using LibCpp2IL.Reflection;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppRGCTXDefinition : ReadableClass
{
    public Il2CppRGCTXDataType type;
    public int _rawIndex;

    public int MethodIndex => type == Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CONSTRAINED ? _constrainedData.MethodIndex : _defData.MethodIndex;

    public int TypeIndex => type == Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CONSTRAINED ? _constrainedData.TypeIndex : _defData.TypeIndex;

    public Il2CppMethodSpec? MethodSpec => LibCpp2IlMain.Binary?.GetMethodSpec(MethodIndex);

    public Il2CppTypeReflectionData? Type => LibCpp2ILUtils.GetTypeReflectionData(LibCpp2IlMain.Binary!.GetType(TypeIndex));


    public class Il2CppRGCTXDefinitionData : ReadableClass
    {
        private int rgctxDataDummy;
        public int MethodIndex => rgctxDataDummy;
        public int TypeIndex => rgctxDataDummy;
        public override void Read(ClassReadingBinaryReader reader)
        {
            rgctxDataDummy = reader.ReadInt32();
        }
    }

    public class Il2CppRGCTXConstrainedData : ReadableClass
    {
        public int _typeIndex;
        public int _encodedMethodIndex;
        public int TypeIndex => _typeIndex;
        public int MethodIndex => _encodedMethodIndex;
   
        public override void Read(ClassReadingBinaryReader reader)
        {
            _typeIndex = reader.ReadInt32();
            _encodedMethodIndex = reader.ReadInt32();
        }
    }
    [Version(Min = 27.2f)]
    private Il2CppRGCTXConstrainedData _constrainedData;

    private Il2CppRGCTXDefinitionData _defData;
    public override void Read(ClassReadingBinaryReader reader)
    {
        type = IsLessThan(29) ? (Il2CppRGCTXDataType)reader.ReadInt32() : (Il2CppRGCTXDataType)reader.ReadInt64();
        if (IsLessThan(27.2f))
        {
            _defData = reader.ReadReadable<Il2CppRGCTXDefinitionData>();
        }
        else
        {
            var va = reader.ReadNUint();
            if (type == Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CONSTRAINED)
            {
                var bakPosition = reader.Position;
                reader.Position = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(va);
                _constrainedData = new Il2CppRGCTXConstrainedData();
                _constrainedData.Read(reader);
                reader.Position = bakPosition;
            }
            else
            {
                var bakPosition = reader.Position;
                reader.Position = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(va);
                _defData = new Il2CppRGCTXDefinitionData();
                _defData.Read(reader);
                reader.Position = bakPosition;
            }

        }

    }
}
