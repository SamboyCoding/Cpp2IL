using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;

namespace LibCpp2IL
{
    public static class LibCpp2IlReflection
    {
        public static Il2CppTypeDefinition? GetType(string name, string? @namespace = null)
        {
            if (LibCpp2IlMain.TheMetadata == null) return null;

            var typeDef = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(td =>
                td.Name == name &&
                (@namespace == null || @namespace == td.Namespace)
            );

            return typeDef;
        }

        public static Il2CppTypeDefinition? GetTypeDefinitionByTypeIndex(int index)
        {
            if (LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.ThePe == null) return null;

            if (index >= LibCpp2IlMain.ThePe.types.Length || index < 0) return null;

            var type = LibCpp2IlMain.ThePe.types[index];
            
            return LibCpp2IlMain.TheMetadata.typeDefs[type.data.classIndex];
        }

        public static int GetTypeIndexFromType(Il2CppTypeDefinition typeDefinition)
        {
            if (LibCpp2IlMain.TheMetadata == null) return -1;

            for (var i = 0; i < LibCpp2IlMain.TheMetadata.typeDefs.Length; i++)
            {
                if (LibCpp2IlMain.TheMetadata.typeDefs[i] == typeDefinition) return i;
            }

            return -1;
        }
        
        public static int GetMethodIndexFromMethod(Il2CppMethodDefinition methodDefinition)
        {
            if (LibCpp2IlMain.TheMetadata == null) return -1;

            for (var i = 0; i < LibCpp2IlMain.TheMetadata.methodDefs.Length; i++)
            {
                if (LibCpp2IlMain.TheMetadata.methodDefs[i] == methodDefinition) return i;
            }

            return -1;
        }
        
        public static int GetFieldIndexFromField(Il2CppFieldDefinition fieldDefinition)
        {
            if (LibCpp2IlMain.TheMetadata == null) return -1;

            for (var i = 0; i < LibCpp2IlMain.TheMetadata.fieldDefs.Length; i++)
            {
                if (LibCpp2IlMain.TheMetadata.fieldDefs[i] == fieldDefinition) return i;
            }

            return -1;
        }
    }
}