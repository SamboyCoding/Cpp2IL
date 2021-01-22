using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;

namespace LibCpp2IL
{
    public static class LibCpp2IlReflection
    {
        private static Dictionary<(string, string?), Il2CppTypeDefinition> _cachedTypes = new Dictionary<(string, string?), Il2CppTypeDefinition>();
        private static Dictionary<string, Il2CppTypeDefinition> _cachedTypesByFullName = new Dictionary<string, Il2CppTypeDefinition>();
        private static Dictionary<Il2CppTypeDefinition, int> _typeIndexes = new Dictionary<Il2CppTypeDefinition, int>();
        public static Il2CppTypeDefinition? GetType(string name, string? @namespace = null)
        {
            if (LibCpp2IlMain.TheMetadata == null) return null;

            var key = (name, @namespace);
            if (!_cachedTypes.ContainsKey(key))
            {
                var typeDef = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(td =>
                    td.Name == name &&
                    (@namespace == null || @namespace == td.Namespace)
                );
                _cachedTypes[key] = typeDef;
            }

            return _cachedTypes[key];
        }
        
        public static Il2CppTypeDefinition? GetTypeByFullName(string fullName)
        {
            if (LibCpp2IlMain.TheMetadata == null) return null;

            if (!_cachedTypesByFullName.ContainsKey(fullName))
            {
                var typeDef = LibCpp2IlMain.TheMetadata.typeDefs.FirstOrDefault(td =>
                    td.FullName == fullName
                );
                _cachedTypesByFullName[fullName] = typeDef;
            }

            return _cachedTypesByFullName[fullName];
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

            lock (_typeIndexes)
            {
                if (!_typeIndexes.ContainsKey(typeDefinition))
                {
                    for (var i = 0; i < LibCpp2IlMain.TheMetadata.typeDefs.Length; i++)
                    {
                        if (LibCpp2IlMain.TheMetadata.typeDefs[i] == typeDefinition)
                        {
                            _typeIndexes[typeDefinition] = i;
                        }
                    }
                }

                return _typeIndexes.GetValueOrDefault(typeDefinition, -1);
            }
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