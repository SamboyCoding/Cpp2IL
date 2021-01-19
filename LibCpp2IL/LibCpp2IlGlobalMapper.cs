using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL
{
    public static class LibCpp2IlGlobalMapper
    {
        internal static List<GlobalIdentifier> TypeRefs = new();
        internal static List<GlobalIdentifier> MethodRefs = new();
        internal static List<GlobalIdentifier> FieldRefs = new();
        internal static List<GlobalIdentifier> Literals = new();

        internal static Dictionary<ulong, GlobalIdentifier> TypeRefsByAddress = new();
        internal static Dictionary<ulong, GlobalIdentifier> MethodRefsByAddress = new();
        internal static Dictionary<ulong, GlobalIdentifier> FieldRefsByAddress = new();
        internal static Dictionary<ulong, GlobalIdentifier> LiteralsByAddress = new();

        internal static void MapGlobalIdentifiers(Il2CppMetadata metadata, PE.PE cppAssembly)
        {
            //Type references
            TypeRefs = metadata.metadataUsageDic[1]
                .Select(kvp => new {kvp, type = cppAssembly.types[kvp.Value]})
                .Select(t => new GlobalIdentifier
                {
                    Name = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type, true),
                    Value = LibCpp2ILUtils.GetTypeReflectionData(t.type)!,
                    Offset = cppAssembly.metadataUsages[t.kvp.Key],
                    IdentifierType = GlobalIdentifier.Type.TYPEREF
                }).ToList();

            //More type references
            TypeRefs.AddRange(metadata.metadataUsageDic[2]
                .Select(kvp => new {kvp, type = cppAssembly.types[kvp.Value]})
                .Select(t => new GlobalIdentifier
                {
                    Name = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type, true),
                    Value = LibCpp2ILUtils.GetTypeReflectionData(t.type)!,
                    Offset = cppAssembly.metadataUsages[t.kvp.Key],
                    IdentifierType = GlobalIdentifier.Type.TYPEREF
                })
            );

            //Method references
            MethodRefs = metadata.metadataUsageDic[3]
                .Select(kvp => new {kvp, method = metadata.methodDefs[kvp.Value]})
                .Select(t => new {t.kvp, t.method, type = metadata.typeDefs[t.method.declaringTypeIdx]})
                .Select(t => new {t.kvp, t.method, typeName = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type)})
                .Select(t => new {t.kvp, t.method, methodName = t.typeName + "." + metadata.GetStringFromIndex(t.method.nameIndex) + "()"})
                .Select(t => new GlobalIdentifier
                {
                    IdentifierType = GlobalIdentifier.Type.METHODREF,
                    Name = t.methodName,
                    Value = t.method,
                    Offset = cppAssembly.metadataUsages[t.kvp.Key]
                }).ToList();

            //Field references
            FieldRefs = metadata.metadataUsageDic[4]
                .Select(kvp => new {kvp, fieldRef = metadata.fieldRefs[kvp.Value]})
                .Select(t => new {t.kvp, t.fieldRef, type = cppAssembly.types[t.fieldRef.typeIndex]})
                .Select(t => new {t.type, t.kvp, t.fieldRef, typeDef = metadata.typeDefs[t.type.data.classIndex]})
                .Select(t => new {t.type, t.kvp, fieldDef = metadata.fieldDefs[t.typeDef.firstFieldIdx + t.fieldRef.fieldIndex]})
                .Select(t => new {t.kvp, t.fieldDef, fieldName = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type, true) + "." + metadata.GetStringFromIndex(t.fieldDef.nameIndex)})
                .Select(t => new GlobalIdentifier
                {
                    IdentifierType = GlobalIdentifier.Type.FIELDREF,
                    Name = t.fieldName,
                    Value = t.fieldDef,
                    Offset = cppAssembly.metadataUsages[t.kvp.Key]
                }).ToList();

            //Literals
            Literals = metadata.metadataUsageDic[5]
                .Select(kvp => new GlobalIdentifier
                {
                    IdentifierType = GlobalIdentifier.Type.LITERAL,
                    Offset = cppAssembly.metadataUsages[kvp.Key],
                    Name = $"{metadata.GetStringLiteralFromIndex(kvp.Value)}",
                    Value = metadata.GetStringLiteralFromIndex(kvp.Value),
                }).ToList();

            //Generic method references
            foreach (var (metadataUsageIdx, methodSpecIdx) in metadata.metadataUsageDic[6]) //kIl2CppMetadataUsageMethodRef
            {
                var methodSpec = cppAssembly.methodSpecs[methodSpecIdx];
                var methodDef = metadata.methodDefs[methodSpec.methodDefinitionIndex];
                var typeDef = metadata.typeDefs[methodDef.declaringTypeIdx];
                var typeName = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, typeDef, true);
                Il2CppTypeReflectionData[] declaringTypeGenericParams = new Il2CppTypeReflectionData[0];
                if (methodSpec.classIndexIndex != -1)
                {
                    var classInst = cppAssembly.genericInsts[methodSpec.classIndexIndex];
                    declaringTypeGenericParams = LibCpp2ILUtils.GetGenericTypeParams(classInst)!;
                    typeName += LibCpp2ILUtils.GetGenericTypeParamNames(metadata, cppAssembly, classInst);
                }

                var methodName = typeName + "." + metadata.GetStringFromIndex(methodDef.nameIndex) + "()";
                Il2CppTypeReflectionData[] genericMethodParameters = new Il2CppTypeReflectionData[0];
                if (methodSpec.methodIndexIndex != -1)
                {
                    var methodInst = cppAssembly.genericInsts[methodSpec.methodIndexIndex];
                    methodName += LibCpp2ILUtils.GetGenericTypeParamNames(metadata, cppAssembly, methodInst);
                    genericMethodParameters = LibCpp2ILUtils.GetGenericTypeParams(methodInst)!;
                }

                MethodRefs.Add(new GlobalIdentifier
                {
                    Name = methodName,
                    Value = new Il2CppGlobalGenericMethodRef
                    {
                        baseMethod = methodDef,
                        declaringType = typeDef,
                        typeGenericParams = declaringTypeGenericParams,
                        methodGenericParams = genericMethodParameters
                    },
                    IdentifierType = GlobalIdentifier.Type.METHODREF,
                    Offset = cppAssembly.metadataUsages[metadataUsageIdx]
                });
            }
            
            foreach (var globalIdentifier in TypeRefs) 
                TypeRefsByAddress[globalIdentifier.Offset] = globalIdentifier;
            
            foreach (var globalIdentifier in MethodRefs) 
                MethodRefsByAddress[globalIdentifier.Offset] = globalIdentifier;
            
            foreach (var globalIdentifier in FieldRefs) 
                FieldRefsByAddress[globalIdentifier.Offset] = globalIdentifier;
            
            foreach (var globalIdentifier in Literals) 
                LiteralsByAddress[globalIdentifier.Offset] = globalIdentifier;
        }
    }
}