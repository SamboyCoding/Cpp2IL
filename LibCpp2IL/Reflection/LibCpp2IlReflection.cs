using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;

namespace LibCpp2IL
{
    public static class LibCpp2IlReflection
    {
        private static readonly Dictionary<(string, string?), Il2CppTypeDefinition> _cachedTypes = new();
        private static readonly Dictionary<string, Il2CppTypeDefinition> _cachedTypesByFullName = new();
        private static readonly Dictionary<Il2CppTypeDefinition, int> _typeIndices = new();
        
        private static readonly Dictionary<Il2CppMethodDefinition, int> _methodIndices = new();
        private static readonly Dictionary<Il2CppFieldDefinition, int> _fieldIndices = new();
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
            if (LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null) return null;

            if (index >= LibCpp2IlMain.Binary.NumTypes || index < 0) return null;

            var type = LibCpp2IlMain.Binary.GetType(index);
            
            return LibCpp2IlMain.TheMetadata.typeDefs[type.data.classIndex];
        }

        public static int GetTypeIndexFromType(Il2CppTypeDefinition typeDefinition)
        {
            if (LibCpp2IlMain.TheMetadata == null) return -1;

            lock (_typeIndices)
            {
                if (!_typeIndices.ContainsKey(typeDefinition))
                {
                    for (var i = 0; i < LibCpp2IlMain.TheMetadata.typeDefs.Length; i++)
                    {
                        if (LibCpp2IlMain.TheMetadata.typeDefs[i] == typeDefinition)
                        {
                            _typeIndices[typeDefinition] = i;
                        }
                    }
                }

                return _typeIndices.GetValueOrDefault(typeDefinition, -1);
            }
        }
        
        // ReSharper disable InconsistentlySynchronizedField
        public static int GetMethodIndexFromMethod(Il2CppMethodDefinition methodDefinition)
        {
            if (LibCpp2IlMain.TheMetadata == null) return -1;
            
            if (_methodIndices.Count == 0)
            {
                lock (_methodIndices)
                {
                    if (_methodIndices.Count == 0)
                    {
                        //Check again inside lock
                        for (var i = 0; i < LibCpp2IlMain.TheMetadata.methodDefs.Length; i++)
                        {
                            var def = LibCpp2IlMain.TheMetadata.methodDefs[i];
                            _methodIndices[def] = i;
                        }
                    }
                }
            }

            return _methodIndices.GetValueOrDefault(methodDefinition, -1);
        }
        
        // ReSharper disable InconsistentlySynchronizedField
        public static int GetFieldIndexFromField(Il2CppFieldDefinition fieldDefinition)
        {
            if (LibCpp2IlMain.TheMetadata == null) return -1;

            if (_fieldIndices.Count == 0)
            {
                lock (_fieldIndices)
                {
                    if (_fieldIndices.Count == 0)
                    {
                        for (var i = 0; i < LibCpp2IlMain.TheMetadata.fieldDefs.Length; i++)
                        {
                            var def = LibCpp2IlMain.TheMetadata.fieldDefs[i];
                            _fieldIndices[def] = i;
                        }
                    }
                }
            }

            return _fieldIndices[fieldDefinition];
        }
    }
}