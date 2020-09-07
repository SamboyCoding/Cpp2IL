using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;

namespace LibCpp2IL
{
    public static class LibCpp2IlGlobalMapper
    {
        internal static List<GlobalIdentifier> TypeRefs = new List<GlobalIdentifier>();
        internal static List<GlobalIdentifier> MethodRefs = new List<GlobalIdentifier>();
        internal static List<GlobalIdentifier> FieldRefs = new List<GlobalIdentifier>();
        internal static List<GlobalIdentifier> Literals = new List<GlobalIdentifier>();

        internal static void MapGlobalIdentifiers(Il2CppMetadata metadata, PE.PE cppAssembly)
        {
            //Type references
            TypeRefs = metadata.metadataUsageDic[1]
                .Select(kvp => new {kvp, type = cppAssembly.types[kvp.Value]})
                .Select(t => new GlobalIdentifier
                {
                    Value = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type, true),
                    Offset = cppAssembly.metadataUsages[t.kvp.Key],
                    IdentifierType = GlobalIdentifier.Type.TYPEREF
                }).ToList();

            //More type references
            TypeRefs.AddRange(metadata.metadataUsageDic[2]
                .Select(kvp => new {kvp, type = cppAssembly.types[kvp.Value]})
                .Select(t => new GlobalIdentifier
                {
                    Value = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type, true),
                    Offset = cppAssembly.metadataUsages[t.kvp.Key],
                    IdentifierType = GlobalIdentifier.Type.TYPEREF
                })
            );

            //Method references
            MethodRefs = metadata.metadataUsageDic[3]
                .Select(kvp => new {kvp, method = metadata.methodDefs[kvp.Value]})
                .Select(t => new {t.kvp, t.method, type = metadata.typeDefs[t.method.declaringTypeIdx]})
                .Select(t => new {t.kvp, t.method, typeName = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type)})
                .Select(t => new {t.kvp, methodName = t.typeName + "." + metadata.GetStringFromIndex(t.method.nameIndex) + "()"})
                .Select(t => new GlobalIdentifier
                {
                    IdentifierType = GlobalIdentifier.Type.METHODREF,
                    Value = t.methodName,
                    Offset = cppAssembly.metadataUsages[t.kvp.Key]
                }).ToList();

            //Field references
            FieldRefs = metadata.metadataUsageDic[4]
                .Select(kvp => new {kvp, fieldRef = metadata.fieldRefs[kvp.Value]})
                .Select(t => new {t.kvp, t.fieldRef, type = cppAssembly.types[t.fieldRef.typeIndex]})
                .Select(t => new {t.type, t.kvp, t.fieldRef, typeDef = metadata.typeDefs[t.type.data.classIndex]})
                .Select(t => new {t.type, t.kvp, fieldDef = metadata.fieldDefs[t.typeDef.firstFieldIdx + t.fieldRef.fieldIndex]})
                .Select(t => new {t.kvp, fieldName = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, t.type, true) + "." + metadata.GetStringFromIndex(t.fieldDef.nameIndex)})
                .Select(t => new GlobalIdentifier
                {
                    IdentifierType = GlobalIdentifier.Type.FIELDREF,
                    Value = t.fieldName,
                    Offset = cppAssembly.metadataUsages[t.kvp.Key]
                }).ToList();

            //Literals
            Literals = metadata.metadataUsageDic[5]
                .Select(kvp => new GlobalIdentifier
                {
                    IdentifierType = GlobalIdentifier.Type.LITERAL,
                    Offset = cppAssembly.metadataUsages[kvp.Key],
                    Value = $"{metadata.GetStringLiteralFromIndex(kvp.Value)}"
                }).ToList();

            //More method references
            foreach (var (metadataUsageIdx, methodSpecIdx) in metadata.metadataUsageDic[6]) //kIl2CppMetadataUsageMethodRef
            {
                var methodSpec = cppAssembly.methodSpecs[methodSpecIdx];
                var methodDef = metadata.methodDefs[methodSpec.methodDefinitionIndex];
                var typeDef = metadata.typeDefs[methodDef.declaringTypeIdx];
                var typeName = LibCpp2ILUtils.GetTypeName(metadata, cppAssembly, typeDef);
                if (methodSpec.classIndexIndex != -1)
                {
                    var classInst = cppAssembly.genericInsts[methodSpec.classIndexIndex];
                    typeName += LibCpp2ILUtils.GetGenericTypeParams(metadata, cppAssembly, classInst);
                }

                var methodName = typeName + "." + metadata.GetStringFromIndex(methodDef.nameIndex) + "()";
                if (methodSpec.methodIndexIndex != -1)
                {
                    var methodInst = cppAssembly.genericInsts[methodSpec.methodIndexIndex];
                    methodName += LibCpp2ILUtils.GetGenericTypeParams(metadata, cppAssembly, methodInst);
                }

                MethodRefs.Add(new GlobalIdentifier
                {
                    Value = methodName,
                    IdentifierType = GlobalIdentifier.Type.METHODREF,
                    Offset = cppAssembly.metadataUsages[metadataUsageIdx]
                });
            }
        }
    }
}