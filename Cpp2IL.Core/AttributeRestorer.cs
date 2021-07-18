#define NO_ATTRIBUTE_RESTORATION_WARNINGS
// #define NO_ATTRIBUTE_ANALYSIS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Analysis.Actions;
using Cpp2IL.Core.Analysis.Actions.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core
{
    public static class AttributeRestorer
    {
        private static readonly ConcurrentDictionary<long, MethodDefinition> _attributeCtorsByClassIndex = new ConcurrentDictionary<long, MethodDefinition>();
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

        internal static void ApplyCustomAttributesToAllTypesInAssembly(AssemblyDefinition assemblyDefinition, KeyFunctionAddresses? keyFunctionAddresses)
        {
            var imageDef = SharedState.ManagedToUnmanagedAssemblies[assemblyDefinition];
            
            foreach (var typeDef in assemblyDefinition.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName))
                RestoreAttributesInType(imageDef, typeDef, keyFunctionAddresses);
        }

        private static void RestoreAttributesInType(Il2CppImageDefinition imageDef, TypeDefinition typeDefinition, KeyFunctionAddresses? keyFunctionAddresses)
        {
            var typeDef = SharedState.ManagedToUnmanagedTypes[typeDefinition];

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

            //This exists only as a guideline for if we should run analysis or not, and a fallback in case we cannot.
            //Maybe in future we should always run analysis? How expensive is that?

            var attributesExpected = GetAttributesFromRange(attributeTypeRange);

            var attributeConstructors = Enumerable.Range(0, attributeTypeRange.count)
                .Select(i => ResolveConstructorForAttribute(attributeTypeRange, i))
                .ToList();

#if NO_ATTRIBUTE_ANALYSIS
            return GenerateAttributesWithoutAnalysis(attributeConstructors, module);
#else

            var mustRunAnalysis = attributeConstructors.Any(c => c.HasParameters);

            if (!mustRunAnalysis)
                //No need to run analysis, so don't
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module);

            if (keyFunctionAddresses == null)
                //Analysis isn't yet supported for ARM.
                //So just generate those which can be generated without params.
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module);

            //Grab generator for this context - be it field, type, method, etc.
            var attributeGeneratorAddress = GetAddressOfAttributeGeneratorFunction(imageDef, attributeTypeRange);

            //Check we can actually map to the binary.
            if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(attributeGeneratorAddress, out _))
                //If not, just generate those which we can (no params).
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module);

            var actions = GetActionsPerformedByGenerator(keyFunctionAddresses, attributeGeneratorAddress, attributesExpected);

            //What we need to do is grab all the LoadAttributeFromAttributeListAction and resolve those locals
            //Then check for any field writes performed on them or function calls on that instance.
            //Also check that each index in the attribute list is present, if not, check that those which are absent have a no-arg constructor.

            //Indexes shared with attributesExpected
            var localArray = new LocalDefinition?[attributesExpected.Count];

            foreach (var action in actions.Where(a => a is LoadAttributeFromAttributeListAction)
                .Cast<LoadAttributeFromAttributeListAction>())
            {
                localArray[action.OffsetInList] = action.LocalMade;
            }

            for (var i = 0; i < attributesExpected.Count; i++)
            {
                var local = localArray[i];
                var attr = attributesExpected[i];
                var noArgCtor = attr.GetConstructors().FirstOrDefault(c => !c.HasParameters);

                if (local == null && noArgCtor != null)
                {
                    //No local made at all, just generate a default attribute and move on.
                    attributes.Add(new CustomAttribute(module.ImportReference(noArgCtor)));
                    continue;
                }

                if (local == null)
                {
                    //No local made at all, BUT we expected constructor params.
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.WarnNewline($"Attribute {attr} in {warningName} of {module.Name} has no zero-argument constructor but no local was made. Falling back to simple attribute generation.");
#endif
                    //Bail out to simple generation.
                    return GenerateAttributesWithoutAnalysis(attributeConstructors, module);
                }

                //We have a local - look for constructor calls and/or field writes.
                var allCtors = attr.GetConstructors().Select(c => c.FullName).ToList();
                var matchingCtorCall = (CallManagedFunctionAction?) actions.FirstOrDefault(c => c is CallManagedFunctionAction {ManagedMethodBeingCalled: { } method} cmfa && cmfa.InstanceBeingCalledOn == local && allCtors.Contains(method.FullName));

                if (matchingCtorCall?.ManagedMethodBeingCalled == null && noArgCtor == null)
                {
                    //No constructor call, BUT we expected constructor params.
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.WarnNewline($"Attribute {attr} in {warningName} of {module.Name} has no zero-argument constructor but no call to a constructor was found. Falling back to simple attribute generation.");
#endif
                    //Bail out to simple generation.
                    return GenerateAttributesWithoutAnalysis(attributeConstructors, module);
                }

                CustomAttribute attributeInstance;
                if (matchingCtorCall?.ManagedMethodBeingCalled != null)
                    try
                    {
                        attributeInstance = GenerateCustomAttributeWithConstructorParams(module.ImportReference(matchingCtorCall.ManagedMethodBeingCalled), matchingCtorCall.Arguments!, module);
                    }
                    catch (Exception e)
                    {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                        Logger.WarnNewline($"Attribute constructor {matchingCtorCall.ManagedMethodBeingCalled} in {warningName} of {module.Name} was resolved with an unprocessable argument. Details: {e.Message}");
#endif
                        //Bail out to simple generation.
                        return GenerateAttributesWithoutAnalysis(attributeConstructors, module);
                    }
                else
                    attributeInstance = new CustomAttribute(module.ImportReference(noArgCtor));

                //TODO Resolve field sets
                var fieldSets = actions.Where(c => c is ImmediateToFieldAction ifa && ifa.InstanceBeingSetOn == local || c is RegToFieldAction rfa && rfa.InstanceWrittenOn == local).ToList();
                if (fieldSets.Count > 0)
                {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.WarnNewline($"Attribute {attr} in {warningName} of {module.Name} has at least one field set action associated with it.");
#endif
                }

                attributes.Add(attributeInstance);
            }

            return attributes;
#endif
        }

        private static List<CustomAttribute> GenerateAttributesWithoutAnalysis(List<MethodReference> attributeCtors, ModuleDefinition module)
        {
            return attributeCtors.Where(c => !c.HasParameters)
                .Select(c => new CustomAttribute(module.ImportReference(c)))
                .ToList();
        }

        private static List<BaseAction> GetActionsPerformedByGenerator(KeyFunctionAddresses? keyFunctionAddresses, ulong attributeGeneratorAddress, List<TypeDefinition> attributesExpected)
        {
            var generatorBody = Utils.GetMethodBodyAtVirtAddressNew(attributeGeneratorAddress, false);

            //Run analysis on this method to get parameters for the various constructors.
            var analyzer = new AsmAnalyzer(attributeGeneratorAddress, generatorBody, keyFunctionAddresses!);
            analyzer.AddParameter(DummyTypeDefForAttributeCache, "attributeCache");
            analyzer.AttributesForRestoration = attributesExpected;

            analyzer.AnalyzeMethod();
            return analyzer.Analysis.Actions;
        }

        private static List<TypeDefinition> GetAttributesFromRange(Il2CppCustomAttributeTypeRange attributeTypeRange)
        {
            var unmanagedAttributes = Enumerable.Range(attributeTypeRange.start, attributeTypeRange.count)
                .Select(attrIdx => LibCpp2IlMain.TheMetadata!.attributeTypes[attrIdx])
                .Select(typeIdx => LibCpp2IlMain.Binary!.GetType(typeIdx))
                .ToList();

            if (unmanagedAttributes.Any(a => a.type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS))
                throw new Exception("Non-class attribute? How does that work?");

            var attributeTypeDefinitions = unmanagedAttributes
                .Select(attributeType => LibCpp2IlMain.TheMetadata!.typeDefs[attributeType.data.classIndex])
                .Select(typeDef => SharedState.UnmanagedToManagedTypes[typeDef])
                .ToList();

            return attributeTypeDefinitions;
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

            lock (_attributeCtorsByClassIndex)
            {
                if (!_attributeCtorsByClassIndex.TryGetValue(attributeType.data.classIndex, out var ret))
                {
                    //First time lookup of this attribute - resolve its constructor.
                    var cppAttribType = LibCpp2IlMain.TheMetadata.typeDefs[attributeType.data.classIndex];

                    var cppMethodDefinition = cppAttribType.Methods!.First(c => c.Name == ".ctor");
                    var managedCtor = cppMethodDefinition.AsManaged();
                    _attributeCtorsByClassIndex.TryAdd(attributeType.data.classIndex, managedCtor);

                    return managedCtor;
                }

                return ret;
            }
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
                            try
                            {
                                value = Utils.CoerceValue(value, destType);
                            }
                            catch (Exception e)
                            {
                                throw new Exception($"Failed to coerce local's known initial value \"{value}\" to type {destType}", e);
                            }

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
                try
                {
                    var toSet = value == null ? null : Utils.CoerceValue(value, typeForArrayToCreateNow);

                    arr.SetValue(toSet, index);
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to coerce value \"{value}\" at index {index} to type {typeForArrayToCreateNow} for array", e);
                }
            }

            return (from object? o in arr select new CustomAttributeArgument(array.ArrayType.ElementType, o)).ToArray();
        }
    }
}