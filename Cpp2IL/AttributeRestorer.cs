using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cpp2IL
{
    public static class AttributeRestorer
    {
        private static readonly Dictionary<long, MethodReference> _attributeCtorsByClassIndex = new Dictionary<long, MethodReference>();
        private static readonly TypeDefinition DummyTypeDefForAttributeCache = new TypeDefinition("dummy", "AttributeCache", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        internal static readonly TypeDefinition DummyTypeDefForAttributeList = new TypeDefinition("dummy", "AttributeList", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        static AttributeRestorer()
        {
            DummyTypeDefForAttributeCache.BaseType = Utils.TryLookupTypeDefKnownNotGeneric("System.ValueType");
            
            //Add count field
            DummyTypeDefForAttributeCache.Fields.Add(new FieldDefinition("count", FieldAttributes.Public, Utils.Int32Reference));

            //Add attribute list
            DummyTypeDefForAttributeCache.Fields.Add(new FieldDefinition("attributes", FieldAttributes.Public, DummyTypeDefForAttributeList));

            SharedState.FieldsByType[DummyTypeDefForAttributeCache] = new List<FieldInType>
            {
                new FieldInType
                {
                    Definition = DummyTypeDefForAttributeCache.Fields.First(),
                    Name = "count",
                    Offset = 0x00,
                    Static = false,
                    DeclaringType = DummyTypeDefForAttributeCache,
                    FieldType = Utils.Int32Reference
                },
                new FieldInType
                {
                    Definition = DummyTypeDefForAttributeCache.Fields.Last(),
                    Name = "attributes",
                    Offset = LibCpp2IlMain.Binary!.is32Bit ? 4ul : 8ul,
                    Static = false,
                    DeclaringType = DummyTypeDefForAttributeCache,
                    FieldType = DummyTypeDefForAttributeList
                },
            };

            SharedState.FieldsByType[DummyTypeDefForAttributeList] = new List<FieldInType>();
        }

        public static void ApplyCustomAttributesToAllTypesInAssembly(Il2CppImageDefinition imageDef, KeyFunctionAddresses? keyFunctionAddresses)
        {
            //Reset this per-module.
            _attributeCtorsByClassIndex.Clear();

            foreach (var typeDef in imageDef.Types!)
                RestoreAttributesInType(imageDef, typeDef, keyFunctionAddresses);
        }

        private static void RestoreAttributesInType(Il2CppImageDefinition imageDef, Il2CppTypeDefinition typeDef, KeyFunctionAddresses? keyFunctionAddresses)
        {
            var typeDefinition = SharedState.UnmanagedToManagedTypes[typeDef];
            
            if(typeDefinition.FullName == "System.Activator")
                Console.WriteLine("Break");

            //Apply custom attributes to type itself
            GetCustomAttributesByAttributeIndex(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, keyFunctionAddresses)
                .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

            //Apply custom attributes to fields
            foreach (var fieldDef in typeDef.Fields!)
            {
                var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                GetCustomAttributesByAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, keyFunctionAddresses)
                    .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
            }

            //Apply custom attributes to methods
            foreach (var methodDef in typeDef.Methods!)
            {
                var methodDefinition = SharedState.UnmanagedToManagedMethods[methodDef];

                GetCustomAttributesByAttributeIndex(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, keyFunctionAddresses)
                    .ForEach(attribute => methodDefinition.CustomAttributes.Add(attribute));
            }
        }

        public static List<CustomAttribute> GetCustomAttributesByAttributeIndex(Il2CppImageDefinition imageDef, int attributeIndex, uint token, ModuleDefinition module, KeyFunctionAddresses? keyFunctionAddresses)
        {
            var attributes = new List<CustomAttribute>();

            //Get attributes and look for the serialize field attribute.
            var attributeTypeRange = LibCpp2IlMain.TheMetadata!.GetCustomAttributeData(imageDef, attributeIndex, token);

            if (attributeTypeRange == null)
                return attributes;

            //At AttributeGeneratorAddress there'll be a series of function calls, each one essentially taking the attribute type and its constructor params.
            //Float values are obtained using BitConverter.ToSingle(byte[], 0) with the 4 bytes making up the float.
            //FUTURE: Do we want to try to get the values for these?

            //The easiest way to do this is to grab all the constructors first, so we know what we're looking for, then run through the analysis engine if we need to.
            var attributeConstructors = Enumerable.Range(0, attributeTypeRange.count)
                .Select(i => ResolveConstructorForAttribute(attributeTypeRange, i))
                .ToList();

            var mustRunAnalysis = attributeConstructors.Any(c => c.HasParameters);

            if (mustRunAnalysis)
            {
                if (LibCpp2IlMain.Binary!.InstructionSet != InstructionSet.X86_32 && LibCpp2IlMain.Binary.InstructionSet != InstructionSet.X86_64)
                    //Filter out constructors which take parameters, as analysis isn't yet supported for ARM.
                    attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
                else
                {
                    //Grab generator for this context - be it field, type, method, etc.
                    var attributeGeneratorAddress = GetAddressOfAttributeGeneratorFunction(imageDef, attributeTypeRange);

                    if (LibCpp2IlMain.Binary.TryMapVirtualAddressToRaw(attributeGeneratorAddress, out _))
                    {
                        //Check we can actually map to the binary.
                        var generatorBody = Utils.GetMethodBodyAtVirtAddressNew(LibCpp2IlMain.Binary, attributeGeneratorAddress, false);

                        //Run analysis on this method to get parameters for the various constructors.
                        var analyzer = new AsmAnalyzer(attributeGeneratorAddress, generatorBody, keyFunctionAddresses!);
                        
                        //TODO: Note to self - this will not work, will have to do some special-case stuff, because the attribute array starts at offset 0 vs 0x10 which is where managed arrays start.
                        analyzer.AddParameter(DummyTypeDefForAttributeCache, "attributeCache");

                        analyzer.AttributeCtorsForRestoration = attributeConstructors;

                        analyzer.AnalyzeMethod();

                        //TODO How does this look on pre-27? 
                        //On v27, this is: 
                        //  Initialise metadata for type, if this has not been done (which we can skip, assuming we have Key Function Addresses, which we don't at this point currently.)
                        //  Then a series of:
                        //    Load of relevant attribute instance from CustomAttributeCache object (which is the generator function's only param)
                        //    Load any System.Type instances required for ctor, using il2cpp_type_get_object (should be exported, equivalent of `typeof`)
                        //    Load simple params for constructor (ints, bools, etc) 
                        //    Call constructor of attribute, on instance from cache, with arguments.
                        //  Notably, the constructors are called in the same order as the attributes are defined in the metadata.
                    }

                    //TODO Remove this line once analysis in
                    attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
                }
            }
            
            attributes.AddRange(attributeConstructors.Select(c => new CustomAttribute(module.ImportReference(c))));

            return attributes;
        }

        private static ulong GetAddressOfAttributeGeneratorFunction(Il2CppImageDefinition imageDef, Il2CppCustomAttributeTypeRange attributeTypeRange)
        {
            var rangeIndex = Array.IndexOf(LibCpp2IlMain.TheMetadata!.attributeTypeRanges, attributeTypeRange);
            ulong attributeGeneratorAddress;
            if (LibCpp2IlMain.MetadataVersion < 27)
                attributeGeneratorAddress = LibCpp2IlMain.Binary!.GetCustomAttributeGenerator(rangeIndex);
            else
            {
                var baseAddress = LibCpp2IlMain.Binary!.GetCodegenModuleByName(imageDef.Name!)!.customAttributeCacheGenerator;
                var ptrToAddress = baseAddress + (ulong) rangeIndex * (LibCpp2IlMain.Binary.is32Bit ? 4ul : 8ul);
                attributeGeneratorAddress = LibCpp2IlMain.Binary.ReadClassAtVirtualAddress<ulong>(ptrToAddress);
            }

            return attributeGeneratorAddress;
        }

        private static MethodReference ResolveConstructorForAttribute(Il2CppCustomAttributeTypeRange attributeTypeRange, int attributeIdx)
        {
            var attributeTypeIndex = LibCpp2IlMain.TheMetadata!.attributeTypes[attributeTypeRange.start + attributeIdx];
            var attributeType = LibCpp2IlMain.Binary!.GetType(attributeTypeIndex);

            if (attributeType.type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS)
                throw new Exception("Non-class attribute? How does that work?");

            if (!_attributeCtorsByClassIndex.ContainsKey(attributeType.data.classIndex))
            {
                //First time lookup of this attribute - resolve its constructor.
                var cppAttribType = LibCpp2IlMain.TheMetadata.typeDefs[attributeType.data.classIndex];

                var cppMethodDefinition = cppAttribType.Methods!.First();
                var managedCtor = cppMethodDefinition.AsManaged();
                _attributeCtorsByClassIndex[attributeType.data.classIndex] = managedCtor;
            }

            return _attributeCtorsByClassIndex[attributeType.data.classIndex];
        }

        private static void GenerateBlobForAttribute()
        {
            //By far the most annoying part of this is that managed attribute parameters are defined as blobs, not as actual data.
            //The CustomAttribute constructor takes a method ref for the attribute's constructor, and the blob. That's our only option - cecil doesn't 
            //even attempt to parse the blob, itself, at any point.

            //Format of the blob - from c058046_ISO_IEC_23271_2012(E) section II.23.3:
            //It's little-endian
            //Always a prolog of 0x0001, or LE: 01 00
            //Then the fixed constructor params, in raw format.
            //  - If this is an array, it's preceded by the number of elements as an unsigned int32.
            //    - This can also be FF FF FF FF to indicate a null value.
            //  - If this is a simple type, it's directly inline (bools are one byte, chars and shorts 2, ints 4, etc)
            //  - If this is a string, it's a count of bytes, with FF indicating null string, and 00 indicating empty string, and then the raw chars
            //      - Remember each char is two bytes.
            //  - If this is a System.Type, it's stored as the canonical name of the type, optionally followed by the assembly, including its version, culture, and public-key token, if defined.
            //      - I.e. it's a string, see above.
            //      - If the assembly name isn't specified, it's assumed to be a) in the currently assembly, or then b) in mscorlib, if it's not found in the current assembly.
            //          - Not specifying it otherwise is an error.
            //  - If the constructor parameter type is defined as System.Object, the blob contains:
            //      - Some information about the type of this data:
            //        - If this is a boxed enum, the byte 0x55 followed by a string, as above.
            //        - Otherwise, If this is a boxed value type, the byte 0x51, then the data as below.
            //        - Else if this is a single-dimensional, zero-based array, the byte 1D, then the data as below
            //        - Regardless of if either of the *two* above conditions are true, there is then a byte indicating element type (see Lib's Il2CppTypeEnum, valid values are TYPE_BOOLEAN to TYPE_STRING)
            //      - This is then followed by the raw "unboxed" value, as one defined above (simple type or string).
            //      - Nulls are NOT supported in this case.
            //Then the number of named-argument pairs, which MUST be present, and is TWO bytes. If there are none, this is 00 00.
            //Then the named arguments, if any:
            //  - Named arguments are similar, except each is preceded by the byte 0x53, indicating a named field, or 0x54, indicating a named property.
            //  - This is followed by the element type, again a byte, Lib's Il2CppTypeEnum, TYPE_BOOLEAN to TYPE_STRING
            //  - Then the name of the field or property
            //  - Finally the same data format as the fixed argument above.
        }
    }
}