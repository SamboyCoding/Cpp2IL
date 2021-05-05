using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis;
using Cpp2IL.Analysis.Actions.Important;
using Cpp2IL.Analysis.ResultModels;
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
        private static readonly byte[] DefaultBlob = {01, 00, 00, 00};

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

            //Apply custom attributes to type itself
            GetCustomAttributesByAttributeIndex(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, keyFunctionAddresses, typeDef.FullName)
                .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

            //Apply custom attributes to fields
            foreach (var fieldDef in typeDef.Fields!)
            {
                var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                GetCustomAttributesByAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, keyFunctionAddresses, fieldDef.Name)
                    .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
            }

            //Apply custom attributes to methods
            foreach (var methodDef in typeDef.Methods!)
            {
                var methodDefinition = SharedState.UnmanagedToManagedMethods[methodDef];

                GetCustomAttributesByAttributeIndex(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, keyFunctionAddresses, methodDef.Name)
                    .ForEach(attribute => methodDefinition.CustomAttributes.Add(attribute));
            }
        }

        public static List<CustomAttribute> GetCustomAttributesByAttributeIndex(Il2CppImageDefinition imageDef, int attributeIndex, uint token, ModuleDefinition module, KeyFunctionAddresses? keyFunctionAddresses, string warningName)
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
            var constructorArgs = new Dictionary<MethodReference, byte[]?>();

            if (mustRunAnalysis)
            {
#if ALLOW_ATTRIBUTE_ANALYSIS
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
                        analyzer.AddParameter(DummyTypeDefForAttributeCache, "attributeCache");
                        analyzer.AttributeCtorsForRestoration = attributeConstructors;

                        analyzer.AnalyzeMethod();

                        //TODO Does this work on pre-27? 

                        var constructorCalls = analyzer.Analysis.Actions.Where(o => o is CallManagedFunctionAction).Cast<CallManagedFunctionAction>().ToList();

                        if (constructorCalls.Count != attributeConstructors.Count || constructorCalls.Any(c => c.ManagedMethodBeingCalled == null || c.Arguments?.Count != c.ManagedMethodBeingCalled?.Parameters.Count || c.Arguments.Any(c => c is null)))
                        {
                            Console.WriteLine($"Warning: failed to recover all attribute constructor parameters for {warningName}: Managed function call synopsis entries are: \n\t{string.Join("\n\t", constructorCalls.Select(c => c.GetSynopsisEntry()))}");
                            attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
                        }
                        else
                        {
                            foreach (var attributeConstructor in attributeConstructors)
                            {
                                var methodCall = constructorCalls.FirstOrDefault(c => c.ManagedMethodBeingCalled == attributeConstructor);

                                if (methodCall == null)
                                {
                                    Console.WriteLine($"Warning: Couldn't find managed ctor call for {attributeConstructor} in {warningName}. Managed function call synopsis entries are: \n\t{string.Join("\n\t", constructorCalls.Select(c => c.GetSynopsisEntry()))}");
                                    attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
                                    constructorArgs.Clear();
                                    break;
                                }

                                try
                                {
                                    constructorArgs[attributeConstructor] = GenerateBlobForAttribute(attributeConstructor, methodCall.Arguments!, module.Name);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Warning: Failed to generate blob for attribute {attributeConstructor} in {warningName}. Details: {e.Message}");
                                    attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
                                    constructorArgs.Clear();
                                    break;
                                }
                            }
                        }
                    }
                }
#else
                attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
#endif
            }

            attributes.AddRange(attributeConstructors.Select(c => new CustomAttribute(module.ImportReference(c), ((IDictionary<MethodReference, byte[]?>) constructorArgs).GetValueOrDefault(c, DefaultBlob))));

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

        private static byte[]? GenerateBlobForAttribute(MethodReference constructor, List<IAnalysedOperand> constructorArgs, string moduleName)
        {
            if (constructorArgs.Count == 0)
                return null;

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

            //Prolog
            var ret = new List<byte> {0x01, 0x00};

            if (constructor.Parameters.Count != constructorArgs.Count)
                throw new Exception("Mismatch between constructor param count & actual args count? Probably because named args support not implemented");

            var i = 0;
            foreach (var analysedOperand in constructorArgs)
            {
                var actualArg = constructor.Parameters[i];
                switch (analysedOperand)
                {
                    case ConstantDefinition cons:
                    {
                        var value = cons.Value;

                        var destType = actualArg.ParameterType.Resolve()?.IsEnum == true ? actualArg.ParameterType.Resolve().GetEnumUnderlyingType() : actualArg.ParameterType;

                        if (cons.Type.FullName != destType.FullName)
                            value = CoerceValue(value, destType);

                        ret.AddRange(value.GetBytesForAttributeBlob(moduleName));
                        break;
                    }
                    case LocalDefinition local:
                    {
                        if (local.KnownInitialValue == null)
                            throw new Exception($"Can't convert a local without a known initial value to a blob: {local}");

                        var value = local.KnownInitialValue;

                        var destType = actualArg.ParameterType.Resolve()?.IsEnum == true ? actualArg.ParameterType.Resolve().GetEnumUnderlyingType() : actualArg.ParameterType;

                        if (local.Type.FullName != destType.FullName)
                            value = CoerceValue(value, destType);

                        ret.AddRange(value.GetBytesForAttributeBlob(moduleName));
                        break;
                    }
                    default:
                        throw new Exception($"Operand {analysedOperand} is not valid for use in a attribute ctor blob");
                }

                i++;
            }

            //Number of named pairs.
            //TODO
            ret.Add(0);
            ret.Add(0);

            return ret.ToArray();
        }

        private static object CoerceValue(object value, TypeReference parameterType)
        {
            //Definitely both primitive
            switch (parameterType.Name)
            {
                case "Boolean":
                    return Convert.ToInt32(value) == 1;
                case "SByte":
                    return Convert.ToSByte(value);
                case "Byte":
                    return Convert.ToByte(value);
                case "Int16":
                    return Convert.ToInt16(value);
                case "UInt16":
                    return Convert.ToUInt16(value);
                case "Int32":
                    return Convert.ToInt32(value);
                case "UInt32":
                    return Convert.ToUInt32(value);
                case "Int64":
                    return Convert.ToInt64(value);
                case "UInt64":
                    return Convert.ToUInt64(value);
            }

            throw new Exception($"Can't coerce {value} to {parameterType}");
        }
    }
}