using System;
using System.Linq;
using System.Reflection;
using LibCpp2IL.PE;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
{
    public class Il2CppEventDefinition
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

        public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

        public Il2CppType? RawType => LibCpp2IlMain.Binary?.GetType(typeIndex);
        
        public Il2CppTypeReflectionData? EventType => LibCpp2IlMain.Binary == null ? null : LibCpp2ILUtils.GetTypeReflectionData(RawType!);

        public EventAttributes EventAttributes => (EventAttributes) RawType!.attrs;

        public Il2CppMethodDefinition? Adder => LibCpp2IlMain.TheMetadata == null || add < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.firstMethodIdx + add];
        
        public Il2CppMethodDefinition? Remover => LibCpp2IlMain.TheMetadata == null || remove < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.firstMethodIdx + remove];
        
        public Il2CppMethodDefinition? Invoker => LibCpp2IlMain.TheMetadata == null || raise < 0 || DeclaringType == null ? null : LibCpp2IlMain.TheMetadata.methodDefs[DeclaringType.firstMethodIdx + raise];
    }
}