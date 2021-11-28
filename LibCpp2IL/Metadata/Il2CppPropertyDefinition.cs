using System;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
{
    public class Il2CppPropertyDefinition
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

        public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

        public Il2CppMethodDefinition? Getter => LibCpp2IlMain.TheMetadata == null || get < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.firstMethodIdx + get];

        public Il2CppMethodDefinition? Setter => LibCpp2IlMain.TheMetadata == null || set < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.firstMethodIdx + set];

        public Il2CppTypeReflectionData? PropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter == null ? Setter!.Parameters![0].Type : Getter!.ReturnType;
        
        public Il2CppType? RawPropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter == null ? Setter!.Parameters![0].RawType : Getter!.RawReturnType;
        
        public bool IsStatic => Getter == null ? Setter!.IsStatic : Getter!.IsStatic;
    }
}