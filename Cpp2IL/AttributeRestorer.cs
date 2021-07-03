#define ALLOW_ATTRIBUTE_ANALYSIS
#define NO_ATTRIBUTE_RESTORATION_WARNINGS

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
            GetCustomAttributesByAttributeIndex(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, keyFunctionAddresses, typeDef.FullName!)
                .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

            //Apply custom attributes to fields
            foreach (var fieldDef in typeDef.Fields!)
            {
                var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                GetCustomAttributesByAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, keyFunctionAddresses, fieldDefinition.FullName)
                    .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
            }

            //Apply custom attributes to methods
            foreach (var methodDef in typeDef.Methods!)
            {
                var methodDefinition = methodDef.AsManaged();

                GetCustomAttributesByAttributeIndex(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, keyFunctionAddresses, methodDefinition.FullName)
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

            if (mustRunAnalysis)
            {
#if ALLOW_ATTRIBUTE_ANALYSIS
                if (LibCpp2IlMain.Binary!.InstructionSet != InstructionSet.X86_32 && LibCpp2IlMain.Binary.InstructionSet != InstructionSet.X86_64)
                    //Filter out constructors which take parameters, as analysis isn't yet supported for ARM.
                    //Add all no-param ones only
                    attributes.AddRange(attributeConstructors.Where(c => !c.HasParameters)
                        .Select(c => new CustomAttribute(module.ImportReference(c))));
                else
                {
                    //Grab generator for this context - be it field, type, method, etc.
                    var attributeGeneratorAddress = GetAddressOfAttributeGeneratorFunction(imageDef, attributeTypeRange);

                    if (LibCpp2IlMain.Binary.TryMapVirtualAddressToRaw(attributeGeneratorAddress, out _))
                    {
                        //Check we can actually map to the binary.
                        var generatorBody = Utils.GetMethodBodyAtVirtAddressNew(attributeGeneratorAddress, false);

                        //Run analysis on this method to get parameters for the various constructors.
                        var analyzer = new AsmAnalyzer(attributeGeneratorAddress, generatorBody, keyFunctionAddresses!);
                        analyzer.AddParameter(DummyTypeDefForAttributeCache, "attributeCache");
                        analyzer.AttributeCtorsForRestoration = attributeConstructors;

                        analyzer.AnalyzeMethod();

                        var allCppCalls = analyzer.Analysis.Actions.Where(o => o is CallManagedFunctionAction).Cast<CallManagedFunctionAction>().ToList();
                        var constructorCalls = allCppCalls.Where(a => a.ManagedMethodBeingCalled?.Name == ".ctor").ToList();

                        //TODO any calls to set_Blah need to be set as fields in the attribute.

                        if (constructorCalls.Count > attributeConstructors.Count)
                            //Take only the first n that we need
                            constructorCalls = constructorCalls.Take(attributeConstructors.Count).ToList();

                        if (constructorCalls.Count == 0)
                        {
                            //Some {projects/versions/compile options/not sure} don't call constructors, they just set fields. Don't know why, but I'm not dealing right now
                            //Some others (audica) don't call ctors for no-arg attributes, only for those with parameters.
                            //TODO implement support for this

                            //For now, add all no-param ones only
                            attributes.AddRange(attributeConstructors.Where(c => !c.HasParameters)
                                .Select(c => new CustomAttribute(module.ImportReference(c))));
                        }
                        else if (constructorCalls.Count != attributeConstructors.Count || constructorCalls.Any(c => c.ManagedMethodBeingCalled == null || c.Arguments?.Count != c.ManagedMethodBeingCalled?.Parameters.Count || c.Arguments!.Any(op => op is null)))
                        {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                            Console.WriteLine(
                                $"Warning: failed to recover all attribute constructor parameters for {warningName} in {module.Name}: Expecting these attributes: {attributeConstructors.Select(a => a.DeclaringType).ToStringEnumerable()}.\n Managed function call synopsis entries are: \n\t{string.Join("\n\t", constructorCalls.Select(c => c.GetSynopsisEntry()))}");
#endif

                            //Add all no-param ones only
                            attributes.AddRange(attributeConstructors.Where(c => !c.HasParameters)
                                .Select(c => new CustomAttribute(module.ImportReference(c))));
                        }
                        else
                        {
                            var seenCountMap = new Dictionary<MethodReference, int>();

                            foreach (var attributeConstructor in attributeConstructors)
                            {
                                var ctor = attributeConstructor;

                                //Find method by declaring type and name.
                                //Don't just match method directly because some ctors (Obsolete) can contain multiple constructors.
                                var matchingByName = constructorCalls.Where(c => c.ManagedMethodBeingCalled?.DeclaringType?.FullName == attributeConstructor.DeclaringType?.FullName && c.ManagedMethodBeingCalled?.Name == attributeConstructor.Name).ToList();

                                CallManagedFunctionAction? methodCall;
                                if (!seenCountMap.ContainsKey(ctor))
                                {
                                    seenCountMap[ctor] = 1;
                                    methodCall = matchingByName.FirstOrDefault();
                                }
                                else
                                {
                                    var timesSeen = seenCountMap[ctor];
                                    methodCall = matchingByName.Count > timesSeen ? matchingByName[timesSeen] : null;
                                    seenCountMap[ctor]++;
                                }

                                if (methodCall != null && methodCall.ManagedMethodBeingCalled != attributeConstructor)
                                    //If we found a better constructor, use it.
                                    ctor = methodCall.ManagedMethodBeingCalled ?? ctor;

                                if (methodCall == null)
                                {
                                    //Failed to find this constructor call at all - fall back to adding those w/out params
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                                    Console.WriteLine($"Warning: Couldn't find managed ctor call for {attributeConstructor} in {warningName}. Constructor call synopsis entries are: \n\t{string.Join("\n\t", constructorCalls.Select(c => c.GetSynopsisEntry()))}");
#endif

                                    //Clear attributes 
                                    attributes.Clear();

                                    //Add those without params
                                    attributes.AddRange(attributeConstructors.Where(c => !c.HasParameters)
                                        .Select(c => new CustomAttribute(module.ImportReference(c))));
                                    break;
                                }

                                try
                                {
                                    attributes.Add(GenerateCustomAttributeWithConstructorParams(ctor, methodCall.Arguments!, module));
                                }
                                catch (Exception e)
                                {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                                    Console.WriteLine($"Warning: Failed to parse args for attribute {attributeConstructor} in {warningName}. Details: {e.Message}");
#endif

                                    //Clear list and add no-param ones.
                                    attributes.Clear();
                                    attributes.AddRange(attributeConstructors.Where(c => !c.HasParameters)
                                        .Select(c => new CustomAttribute(module.ImportReference(c))));
                                    break;
                                }
                            }
                        }
                    }
                }
#else
                attributeConstructors = attributeConstructors.Where(c => !c.HasParameters).ToList();
                attributes.AddRange(attributeConstructors.Select(c => new CustomAttribute(module.ImportReference(c))));
#endif
            }

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
                var relativeIndex = rangeIndex - imageDef.customAttributeStart;
                var ptrToAddress = baseAddress + (ulong) relativeIndex * (LibCpp2IlMain.Binary.is32Bit ? 4ul : 8ul);
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

                var cppMethodDefinition = cppAttribType.Methods!.First(c => c.Name == ".ctor");
                var managedCtor = cppMethodDefinition.AsManaged();
                _attributeCtorsByClassIndex[attributeType.data.classIndex] = managedCtor;
            }

            return _attributeCtorsByClassIndex[attributeType.data.classIndex];
        }

        private static CustomAttribute GenerateCustomAttributeWithConstructorParams(MethodReference constructor, List<IAnalysedOperand> constructorArgs, ModuleDefinition module)
        {
            var customAttribute = new CustomAttribute(module.ImportReference(constructor));

            if (constructorArgs.Count == 0)
                return customAttribute;

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
                            value = Utils.CoerceValue(value, destType);

                        customAttribute.ConstructorArguments.Add(new CustomAttributeArgument(destType, value));
                        break;
                    }
                    case LocalDefinition local:
                    {
                        if (local.KnownInitialValue == null)
                            throw new Exception($"Can't use a local without a KnownInitialValue in an attribute ctor: {local}");

                        var value = local.KnownInitialValue;
                        var originalValue = value;

                        var destType = actualArg.ParameterType.Resolve()?.IsEnum == true ? actualArg.ParameterType.Resolve().GetEnumUnderlyingType() : actualArg.ParameterType;

                        if (value is AllocatedArray array)
                            value = AllocateArray(array);
                        else if (local.Type.FullName != destType.FullName)
                            value = Utils.CoerceValue(value, destType);

                        if (destType.FullName == "System.Object")
                        {
                            //Need to wrap value in another CustomAttributeArgument of the pre-casting type.
                            value = new CustomAttributeArgument(Utils.TryLookupTypeDefKnownNotGeneric(originalValue.GetType().FullName), originalValue);
                        }

                        customAttribute.ConstructorArguments.Add(new CustomAttributeArgument(destType, value));
                        break;
                    }
                    default:
                        throw new Exception($"Operand {analysedOperand} is not valid for use in a attribute ctor");
                }

                i++;
            }

            return customAttribute;
        }

        private static object AllocateArray(AllocatedArray array)
        {
            var typeForArrayToCreateNow = array.ArrayType.ElementType;

            if (typeForArrayToCreateNow == null)
                throw new Exception("Array has no type");

            if (typeForArrayToCreateNow.Resolve() is {IsEnum: true} enumType)
                typeForArrayToCreateNow = enumType.GetEnumUnderlyingType() ?? typeForArrayToCreateNow;

            var arrayType = Type.GetType(typeForArrayToCreateNow.FullName) ?? throw new Exception($"Could not resolve array type {array.ArrayType.ElementType.FullName}");
            var arr = Array.CreateInstance(arrayType, array.Size);

            foreach (var (index, value) in array.KnownValuesAtOffsets)
            {
                var toSet = value == null ? null : Utils.CoerceValue(value, typeForArrayToCreateNow);

                arr.SetValue(toSet, index);
            }

            return (from object? o in arr select new CustomAttributeArgument(array.ArrayType.ElementType, o)).ToArray();
        }
    }
}