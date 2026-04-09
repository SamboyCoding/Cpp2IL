using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppRGCTXDefinition : ReadableClass
{
    public Il2CppRGCTXDataType type;
    public int _rawIndex;

    public int MethodIndex => _defData?.MethodIndex ?? _constrainedData!.MethodIndex;

    public int TypeIndex => _defData?.TypeIndex ?? _constrainedData!.TypeIndex;

    public Il2CppMethodSpec? MethodSpec
    {
        get
        {
            return OwningBinary?.GetMethodSpec(MethodIndex);
        }
    }

    public Il2CppTypeReflectionData? Type
    {
        get
        {
            var binary = OwningBinary;
            if (binary == null) return null;
            var t = binary.GetType(Il2CppVariableWidthIndex<Il2CppType>.MakeTemporaryForFixedWidthUsage(TypeIndex));
            return LibCpp2ILUtils.GetTypeReflectionData(t);
        }
    }


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
    private Il2CppRGCTXConstrainedData? _constrainedData;

    private Il2CppRGCTXDefinitionData? _defData;

    public override void Read(ClassReadingBinaryReader reader)
    {
        type = IsLessThan(29) ? (Il2CppRGCTXDataType)reader.ReadInt32() : (Il2CppRGCTXDataType)reader.ReadInt64();
        if (IsLessThan(27.2f))
        {
            _defData = new Il2CppRGCTXDefinitionData();
            _defData.Read(reader);
        }
        else
        {
            var va = reader.ReadNUint();
            var bakPosition = reader.Position;

            var binary = OwningBinary;
            if (binary == null)
            {
                // Can't resolve VA -> raw without a binary context. Leave as-is.
                reader.Position = bakPosition;
                return;
            }

            reader.Position = binary.MapVirtualAddressToRaw(va);

            if (type == Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CONSTRAINED)
            {
                _constrainedData = new Il2CppRGCTXConstrainedData();
                _constrainedData.Read(reader);
            }
            else
            {
                _defData = new Il2CppRGCTXDefinitionData();
                _defData.Read(reader);
            }

            reader.Position = bakPosition;
        }
    }
}
