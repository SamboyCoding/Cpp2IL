using System;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
{
    public class Il2CppFieldDefinition
    {
        public int nameIndex;
        public int typeIndex;
        [Version(Max = 24)] public int customAttributeIndex;
        public uint token;
        
        public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

        public Il2CppType? RawFieldType => LibCpp2IlMain.Binary?.GetType(typeIndex);
        public Il2CppTypeReflectionData? FieldType => RawFieldType == null ? null : LibCpp2ILUtils.GetTypeReflectionData(RawFieldType);

        public int FieldIndex => LibCpp2IlReflection.GetFieldIndexFromField(this);

        public Il2CppFieldDefaultValue? DefaultValue => LibCpp2IlMain.TheMetadata?.GetFieldDefaultValue(this);
        
        public override string ToString()
        {
            if(LibCpp2IlMain.TheMetadata == null) return base.ToString();

            return $"Il2CppFieldDefinition[Name={Name}, FieldType={FieldType}]";
        }

        public byte[] StaticArrayInitialValue
        {
            get
            {
                if (FieldType is not { isArray: false, isPointer: false, isType: true, isGenericType: false })
                    return Array.Empty<byte>();
                
                if (FieldType.baseType!.Name?.StartsWith("__StaticArrayInitTypeSize=") != true)
                    return Array.Empty<byte>();

                var length = int.Parse(FieldType.baseType!.Name.Replace("__StaticArrayInitTypeSize=", ""));
                var (dataIndex, _) = LibCpp2IlMain.TheMetadata!.GetFieldDefaultValue(FieldIndex);

                var pointer = LibCpp2IlMain.TheMetadata!.GetDefaultValueFromIndex(dataIndex);

                if (pointer <= 0) return Array.Empty<byte>();

                var results = LibCpp2IlMain.TheMetadata.ReadByteArrayAtRawAddress(pointer, length);

                return results;
            }
        }
    }
}