#define NO_ATTRIBUTE_RESTORATION_WARNINGS
// #define NO_ATTRIBUTE_ANALYSIS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Analysis.Actions.ARM64;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Exceptions;
using Iced.Intel;
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
        internal static readonly TypeDefinition DummyTypeDefForAttributeCache = new TypeDefinition("dummy", "AttributeCache", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        internal static readonly TypeDefinition DummyTypeDefForAttributeList = new TypeDefinition("dummy", "AttributeList", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        private static readonly ConcurrentDictionary<MethodDefinition, FieldToParameterMapping[]?> FieldToParameterMappings = new ConcurrentDictionary<MethodDefinition, FieldToParameterMapping[]?>();

        static AttributeRestorer() => Initialize();

        private static void Initialize()
        {
            DummyTypeDefForAttributeCache.BaseType = Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.ValueType");

            //Add count field
            DummyTypeDefForAttributeCache.Fields.Add(new FieldDefinition("count", FieldAttributes.Public, Utils.Utils.Int32Reference));

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
                    FieldType = Utils.Utils.Int32Reference
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

        /// <summary>
        /// Must be called after SharedState.Clear
        /// </summary>
        internal static void Reset()
        {
            lock (_attributeCtorsByClassIndex)
            {
                _attributeCtorsByClassIndex.Clear();
            }

            lock (FieldToParameterMappings)
            {
                FieldToParameterMappings.Clear();
            }

            Initialize();
        }

        internal static void ApplyCustomAttributesToAllTypesInAssembly<T>(AssemblyDefinition assemblyDefinition, BaseKeyFunctionAddresses? keyFunctionAddresses)
        {
            var imageDef = SharedState.ManagedToUnmanagedAssemblies[assemblyDefinition];

            foreach (var typeDef in assemblyDefinition.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName))
                RestoreAttributesInType<T>(imageDef, typeDef, keyFunctionAddresses);
        }

        private static void RestoreAttributesInType<T>(Il2CppImageDefinition imageDef, TypeDefinition typeDefinition, BaseKeyFunctionAddresses? keyFunctionAddresses)
        {
            var typeDef = SharedState.ManagedToUnmanagedTypes[typeDefinition];

            //Apply custom attributes to type itself
            GetCustomAttributesByAttributeIndex<T>(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, keyFunctionAddresses, typeDef.FullName!)
                .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

            //Apply custom attributes to fields
            foreach (var fieldDef in typeDef.Fields!)
            {
                var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                GetCustomAttributesByAttributeIndex<T>(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, keyFunctionAddresses, fieldDefinition.FullName)
                    .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
            }

            //Apply custom attributes to methods
            foreach (var methodDef in typeDef.Methods!)
            {
                var methodDefinition = methodDef.AsManaged();

                GetCustomAttributesByAttributeIndex<T>(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, keyFunctionAddresses, methodDefinition.FullName)
                    .ForEach(attribute => methodDefinition.CustomAttributes.Add(attribute));
            }

            //Apply custom attributes to properties
            foreach (var propertyDef in typeDef.Properties!)
            {
                var propertyDefinition = SharedState.UnmanagedToManagedProperties[propertyDef];

                GetCustomAttributesByAttributeIndex<T>(imageDef, propertyDef.customAttributeIndex, propertyDef.token, typeDefinition.Module, keyFunctionAddresses, propertyDefinition.FullName)
                    .ForEach(attribute => propertyDefinition.CustomAttributes.Add(attribute));
            }

            //Nested Types
            foreach (var nestedType in typeDefinition.NestedTypes)
            {
                RestoreAttributesInType<T>(imageDef, nestedType, keyFunctionAddresses);
            }
        }

        public static List<CustomAttribute> GetCustomAttributesByAttributeIndex<T>(Il2CppImageDefinition imageDef, int attributeIndex, uint token, ModuleDefinition module, BaseKeyFunctionAddresses? keyFunctionAddresses, string warningName)
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
            
            if (LibCpp2IlMain.MetadataVersion >= 29)
                //TODO Can't do this on v29 because attributeGeneratorAddress is unknown
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module, 0, false);

            //Grab generator for this context - be it field, type, method, etc.
            var attributeGeneratorAddress = GetAddressOfAttributeGeneratorFunction(imageDef, attributeTypeRange);

            if (!mustRunAnalysis)
                //No need to run analysis, so don't
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module, attributeGeneratorAddress, false);

            if (keyFunctionAddresses == null || LibCpp2IlMain.Binary!.InstructionSet is InstructionSet.ARM32)
                //Analysis isn't yet supported for ARM.
                //So just generate those which can be generated without params.
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module, attributeGeneratorAddress, true);

            //Check we can actually map to the binary.
            if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(attributeGeneratorAddress, out _))
                //If not, just generate those which we can (no params).
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module, 0, true);

            List<BaseAction<T>> actions;
            try
            {
                actions = GetActionsPerformedByGenerator<T>(keyFunctionAddresses, attributeGeneratorAddress, attributesExpected);
            }
            catch (AnalysisExceptionRaisedException e)
            {
                //Ignore, fall back
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.WarnNewline($"Attribute generator for {warningName} of {module.Name} threw exception during analysis: {e.Message}. Falling back to simple generation.");
#endif
                return GenerateAttributesWithoutAnalysis(attributeConstructors, module, attributeGeneratorAddress, true);
            }

            //What we need to do is grab all the LoadAttributeFromAttributeListAction and resolve those locals
            //Then check for any field writes performed on them or function calls on that instance.
            //Also check that each index in the attribute list is present, if not, check that those which are absent have a no-arg constructor.

            //Indexes shared with attributesExpected
            var localArray = new LocalDefinition?[attributesExpected.Count];

            foreach (var action in actions.Where(a => a is AbstractAttributeLoadFromListAction<T>)
                .Cast<AbstractAttributeLoadFromListAction<T>>())
            {
                if (action.LocalMade != null)
                    localArray[action.OffsetInList] = action.LocalMade;
            }

            attributes.AddRange(GenerateAttributesWithoutAnalysis(attributeConstructors, module, attributeGeneratorAddress, false));

            for (var i = 0; i < attributesExpected.Count; i++)
            {
                var local = localArray[i];
                var attr = attributesExpected[i];
                var noArgCtor = attr.GetConstructors().FirstOrDefault(c => !c.HasParameters);

                if (local == null && noArgCtor != null)
                {
                    //Handled by the manual emission of all simple attributes above
                    continue;
                }

                if (local == null)
                {
                    //No local made at all, BUT we expected constructor params.
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.WarnNewline($"Attribute {attr} applied to {warningName} of {module.Name} has no zero-argument constructor but no local was made. Only a fallback Attribute will be added.");
#endif
                    //Give up on this attribute and move to the next one.
                    var fallback = GenerateFallbackAttribute(attr.GetConstructors().First(), module, attributeGeneratorAddress);
                    if (fallback != null)
                        attributes.Add(fallback);
                    continue;
                }

                //We have a local - look for constructor calls and/or field writes.
                var allCtorNames = attr.GetConstructors().Select(c => c.FullName).ToList();
                var matchingCtorCall = (AbstractCallAction<T>?)actions.FirstOrDefault(c => c is AbstractCallAction<T> { ManagedMethodBeingCalled: { } method } cmfa && cmfa.InstanceBeingCalledOn == local && allCtorNames.Contains(method.FullName));

                (MethodDefinition potentialCtor, List<CustomAttributeArgument> parameterList)? hardWayResult = null;
                if (matchingCtorCall?.ManagedMethodBeingCalled == null && noArgCtor == null)
                {
                    //No constructor call, BUT we expected constructor params.

                    //May have been super-optimized.
                    try
                    {
                        hardWayResult = TryResolveAttributeConstructorParamsTheHardWay(keyFunctionAddresses, attr, actions, local);
                    }
                    catch (Exception)
                    {
                        //Suppress and just bail out below
                    }

                    if (hardWayResult == null)
                    {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                        Logger.WarnNewline($"Attribute {attr} applied to {warningName} of {module.Name} has no zero-argument constructor but no call to a constructor was found, and 'hard way' reconstruction failed. Only a fallback Attribute will be added.");
#endif
                        //Give up on this attribute and move to the next one.

                        var fallback = GenerateFallbackAttribute(attr.GetConstructors().First(), module, attributeGeneratorAddress);
                        if (fallback != null)
                            attributes.Add(fallback);
                        continue;
                    }
                }

                CustomAttribute attributeInstance;
                try
                {
                    if (matchingCtorCall?.ManagedMethodBeingCalled != null && matchingCtorCall.Arguments?.Count > 0)
                        attributeInstance = GenerateCustomAttributeWithConstructorParams(module.ImportReference(matchingCtorCall.ManagedMethodBeingCalled), matchingCtorCall.Arguments!, module);
                    else if (hardWayResult != null)
                        attributeInstance = GenerateCustomAttributeFromHardWayResult(hardWayResult.Value.potentialCtor, hardWayResult.Value.parameterList, module);
                    else
                        //Skip simple (no argument) attributes, they've already been generated
                        //Future: need to check field sets here.
                        continue;
                }
                catch (Exception e)
                {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.WarnNewline($"Attribute constructor {matchingCtorCall?.ManagedMethodBeingCalled ?? hardWayResult?.potentialCtor} applied to {warningName} of {module.Name} was resolved with an unprocessable argument. Details: {e.Message}");
#endif
                    //Give up on this attribute and move to the next one.
                    var fallback = GenerateFallbackAttribute(attr.GetConstructors().First(), module, attributeGeneratorAddress);
                    if (fallback != null)
                        attributes.Add(fallback);
                    continue;
                }

                //TODO Resolve field sets, including processing out hard way result params etc.
                var fieldSets = actions.Where(c => c is ImmediateToFieldAction ifa && ifa.InstanceBeingSetOn == local || c is RegToFieldAction rfa && rfa.InstanceBeingSetOn == local).ToList();
                if (fieldSets.Count > 0 && !hardWayResult.HasValue)
                {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    // Logger.WarnNewline($"Attribute {attr} applied to {warningName} of {module.Name} has at least one field set action associated with it.");
#endif
                }

                attributes.Add(attributeInstance);
            }

            return attributes;
#endif
        }

        private static List<CustomAttribute> GenerateAttributesWithoutAnalysis(List<MethodReference> attributeCtors, ModuleDefinition module, ulong generatorPtr, bool generateFallback)
        {
            return attributeCtors
                .Where(c => !c.HasParameters || generateFallback)
                .Select(c => c.HasParameters ? GenerateFallbackAttribute(c, module, generatorPtr) : new(module.ImportReference(c)))
                .Where(c => c != null)
                .ToList()!;
        }

        private static CustomAttribute? GenerateFallbackAttribute(MethodReference constructor, ModuleDefinition module, ulong generatorPtr)
        {
            var attributeType = module.Types.SingleOrDefault(t => t.Namespace == AssemblyPopulator.InjectedNamespaceName && t.Name == "AttributeAttribute");

            if (attributeType == null)
                return null;

            var attributeCtor = attributeType.GetConstructors().First();

            var ca = new CustomAttribute(attributeCtor);
            var name = new CustomAttributeNamedArgument("Name", new(module.ImportReference(Utils.Utils.StringReference), constructor.DeclaringType.Name));
            var rva = new CustomAttributeNamedArgument("RVA", new(module.ImportReference(Utils.Utils.StringReference), $"0x{LibCpp2IlMain.Binary!.GetRVA(generatorPtr):X}"));

            if (!LibCpp2IlMain.Binary.TryMapVirtualAddressToRaw(generatorPtr, out var offsetInBinary))
                offsetInBinary = 0;
            
            var offset = new CustomAttributeNamedArgument("Offset", new(module.ImportReference(Utils.Utils.StringReference), $"0x{offsetInBinary:X}"));

            ca.Fields.Add(name);
            ca.Fields.Add(rva);
            ca.Fields.Add(offset);
            return ca;
        }

        private static List<BaseAction<T>> GetActionsPerformedByGenerator<T>(BaseKeyFunctionAddresses keyFunctionAddresses, ulong attributeGeneratorAddress, List<TypeDefinition> attributesExpected)
        {
            //Nasty generic casting crap
            AsmAnalyzerBase<T> analyzer = (AsmAnalyzerBase<T>)(LibCpp2IlMain.Binary?.InstructionSet switch
            {
                InstructionSet.X86_32 or InstructionSet.X86_64 => (object)new AsmAnalyzerX86(attributeGeneratorAddress, Utils.Utils.GetMethodBodyAtVirtAddressNew(attributeGeneratorAddress, false), keyFunctionAddresses!),
                // InstructionSet.ARM32 => (object) new AsmAnalyzerArmV7(attributeGeneratorAddress, FIX_ME, keyFunctionAddresses!),
                InstructionSet.ARM64 => (object)new AsmAnalyzerArmV8A(attributeGeneratorAddress, Utils.Utils.GetArm64MethodBodyAtVirtualAddress(attributeGeneratorAddress, true), keyFunctionAddresses!),
                _ => throw new UnsupportedInstructionSetException()
            });

            //Run analysis on this method to get parameters for the various constructors.
            analyzer.AddParameter(DummyTypeDefForAttributeCache, "attributeCache");
            analyzer.AttributesForRestoration = attributesExpected;

            analyzer.AnalyzeMethod();
            return analyzer.Analysis.Actions.ToList();
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

            if (rangeIndex < 0)
            {
                Logger.WarnNewline("Found attribute type range that's not in the list we have?");
                return ulong.MaxValue; //Guaranteed to be outside the mappable range, so we fall back to basic restoration
            }

            ulong attributeGeneratorAddress;
            if (LibCpp2IlMain.MetadataVersion < 27)
            {
                try
                {
                    attributeGeneratorAddress = LibCpp2IlMain.Binary!.GetCustomAttributeGenerator(rangeIndex);
                }
                catch (IndexOutOfRangeException)
                {
                    Logger.WarnNewline($"Found attribute type range for token 0x{attributeTypeRange.token:X} at index {rangeIndex} which is beyond the known generator address list (length={LibCpp2IlMain.Binary!.AllCustomAttributeGenerators.Length}).");
                    return ulong.MaxValue;
                }
            }
            else
            {
                var baseAddress = LibCpp2IlMain.Binary!.GetCodegenModuleByName(imageDef.Name!)!.customAttributeCacheGenerator;
                var relativeIndex = rangeIndex - imageDef.customAttributeStart;
                var ptrToAddress = baseAddress + (ulong)relativeIndex * (LibCpp2IlMain.Binary.is32Bit ? 4ul : 8ul);
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

                customAttribute.ConstructorArguments.Add(CoerceAnalyzedOpToParameter(analysedOperand, actualArg));

                i++;
            }

            return customAttribute;
        }

        private static CustomAttributeArgument CoerceAnalyzedOpToParameter(IAnalysedOperand analysedOperand, ParameterDefinition actualArg)
        {
            switch (analysedOperand)
            {
                case ConstantDefinition cons:
                {
                    var value = cons.Value;

                    var destType = actualArg.ParameterType.Resolve()?.IsEnum == true ? actualArg.ParameterType.Resolve().GetEnumUnderlyingType() : actualArg.ParameterType;

                    if (cons.Type.FullName != destType.FullName)
                        value = Utils.Utils.CoerceValue(value, destType);

                    return new CustomAttributeArgument(destType, value);
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
                            value = Utils.Utils.CoerceValue(value, destType);
                        }
                        catch (Exception e)
                        {
                            throw new Exception($"Failed to coerce local's known initial value \"{value}\" to type {destType}", e);
                        }

                    if (destType.FullName == "System.Object")
                    {
                        //Need to wrap value in another CustomAttributeArgument of the pre-casting type.
                        value = new CustomAttributeArgument(Utils.Utils.TryLookupTypeDefKnownNotGeneric(originalValue.GetType().FullName), originalValue);
                    }

                    return new CustomAttributeArgument(destType, value);
                }
                default:
                    throw new Exception($"Operand {analysedOperand} is not valid for use in a attribute ctor");
            }
        }

        private static object AllocateArray(AllocatedArray array)
        {
            var typeForArrayToCreateNow = array.ArrayType.ElementType;

            if (typeForArrayToCreateNow == null)
                throw new Exception("Array has no type");

            if (typeForArrayToCreateNow.Resolve() is { IsEnum: true } enumType)
                typeForArrayToCreateNow = enumType.GetEnumUnderlyingType() ?? typeForArrayToCreateNow;

            var arrayType = Type.GetType(typeForArrayToCreateNow.FullName) ?? throw new Exception($"Could not resolve array type {array.ArrayType.ElementType.FullName}");
            var arr = Array.CreateInstance(arrayType, array.Size);

            if (array.KnownValuesAtOffsets.Count != array.Size)
                throw new Exception($"Failed to populate known array - only have {array.KnownValuesAtOffsets.Count} known values for an array of length {array.Size}.");

            foreach (var (index, value) in array.KnownValuesAtOffsets)
            {
                try
                {
                    var toSet = value == null ? null : Utils.Utils.CoerceValue(value, typeForArrayToCreateNow);

                    arr.SetValue(toSet, index);
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to coerce value \"{value}\" (type {value?.GetType().FullName}) at index {index} to type {typeForArrayToCreateNow} for array of length {array.Size}", e);
                }
            }

            return (from object? o in arr select new CustomAttributeArgument(array.ArrayType.ElementType, o)).ToArray();
        }

        private static FieldToParameterMapping[]? TryAnalyzeAttributeConstructorToResolveFieldWrites(MethodDefinition constructor, BaseKeyFunctionAddresses keyFunctionAddresses)
        {
            //Some games have optimization dialed up to 11 - this results in attribute generator functions not actually calling constructors.
            //Instead, the constructor is inlined, and the field writes are copied directly into the generator.
            //So we can run analysis on the constructor, resolve field writes, and see if that matches with what we have in the attribute generator.
            //Then we get a mapping from field to constructor, parameter, so we can map the field writes in the generator function back to constructor params.

            lock (FieldToParameterMappings)
            {
                if (FieldToParameterMappings.TryGetValue(constructor, out var ret))
                    return ret;

                Logger.VerboseNewline($"Attempting to run attribute constructor reconstruction for {constructor.FullName}", "AttributeRestore");
                var methodPointer = constructor.AsUnmanaged().MethodPointer;
                var analyzer = new AsmAnalyzerX86(constructor, methodPointer, keyFunctionAddresses);

                var fail = false;
                List<RegToFieldAction>? fieldWrites = null;
                try
                {
                    analyzer.AnalyzeMethod();

                    //Grab field writes specifically from registers.
                    fieldWrites = analyzer.Analysis.Actions.Where(f => f is RegToFieldAction).Cast<RegToFieldAction>().ToList();

                    if (!fieldWrites.All(f => f.ValueRead is LocalDefinition { ParameterDefinition: { } }))
                    {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.VerboseNewline($"\t{constructor.FullName} has a local => field where the local isn't a parameter.");
#endif
                        fail = true;
                    }

                    if (fieldWrites.Any(f => f.FieldWritten?.FinalLoadInChain == null))
                    {
#if !NO_ATTRIBUTE_RESTORATION_WARNINGS
                    Logger.VerboseNewline($"\t{constructor.FullName} has a local => field where the field is non-simple.");
#endif
                        fail = true;
                    }
                }
                catch (Exception e)
                {
                    Logger.WarnNewline($"Failed to run constructor restoration: {e}.");
                    fail = true;
                }

                if (fail)
                {
                    FieldToParameterMappings.TryAdd(constructor, null);
                    return null;
                }

                ret = fieldWrites!.Select(f => new FieldToParameterMapping(f.FieldWritten!.FinalLoadInChain!, ((LocalDefinition)f.ValueRead!).ParameterDefinition!)).ToArray();

                FieldToParameterMappings.TryAdd(constructor, ret);
                return ret;
            }
        }

        private static (MethodDefinition potentialCtor, List<CustomAttributeArgument> parameterList)? TryResolveAttributeConstructorParamsTheHardWay<T>(BaseKeyFunctionAddresses keyFunctionAddresses, TypeDefinition attr, List<BaseAction<T>> actions, LocalDefinition? local)
        {
            if (typeof(T) != typeof(Instruction))
                return null;

            //Try and get mappings for all constructors.
            var allPotentialCtors = attr.GetConstructors()
                .Where(f => !f.IsStatic)
                .Select(c => (c!, TryAnalyzeAttributeConstructorToResolveFieldWrites(c, keyFunctionAddresses)))
                .Where(pair => pair.Item2 != null)
                .ToList();

            //And get all field writes on this attribute
            var allWritesInGeneratorOnThisAttribute = actions
                .Where(a => (a is AbstractFieldWriteAction<T> afwa && afwa.InstanceBeingSetOn == local))
                .Cast<AbstractFieldWriteAction<T>>()
                .ToList();

            if (allWritesInGeneratorOnThisAttribute.Any(w => w.FieldWritten?.FinalLoadInChain == null))
            {
                //complex field writes (that is to say, writes on a field of a field) are not yet implemented (do I ever want to?)
                //so this is a failure condition
                return null;
            }

            //Sort by number of mappings, descending, so we get most relevant first.
            allPotentialCtors.SortByExtractedKey(pair => pair.Item2!.Length);
            allPotentialCtors.Reverse();

            //Check that mappings and field writes line up
            foreach (var (potentialCtor, mappings) in allPotentialCtors)
            {
                if (mappings == null)
                    continue;

                //Going to assume that the order is preserved, because it bloody well should be.
                //Check that the first n field writes in the generator (where n is number of mappings for the ctor)
                //match, in order, all n fields being written in the constructor.
                var matches = allWritesInGeneratorOnThisAttribute
                    .Take(mappings.Length)
                    .Select(w => w.FieldWritten!.FinalLoadInChain!)
                    .SequenceEqual(mappings.Select(m => m.Field));

                if (!matches)
                    continue; //Move to next potential ctor

                //This constructor matches, insofar as there are the same number of field writes, and each field written in the ctor is also written, in the same order, in the generator.
                //So we need to extract the parameters.

                var parameterList = new List<CustomAttributeArgument>();
                foreach (var parameter in potentialCtor.Parameters)
                {
                    var mapping = mappings.FirstOrDefault(m => m.Parameter == parameter);

                    if (mapping.Parameter == null!)
                        return null;

                    var fieldWrite = allWritesInGeneratorOnThisAttribute.FirstOrDefault(w => w.FieldWritten!.FinalLoadInChain == mapping.Field);

                    if (fieldWrite == null)
                        return null;

                    var destType = parameter.ParameterType.Resolve()?.IsEnum == true ? parameter.ParameterType.Resolve().GetEnumUnderlyingType() : parameter.ParameterType;
                    switch (fieldWrite)
                    {
                        case ImmediateToFieldAction i:
                        {
                            var value = i.ConstantValue;

                            if (value.GetType().FullName != destType.FullName)
                                value = Utils.Utils.CoerceValue(value, destType);

                            if (destType.FullName == "System.Object")
                            {
                                //Need to wrap value in another CustomAttributeArgument of the pre-casting type.
                                value = new CustomAttributeArgument(Utils.Utils.TryLookupTypeDefKnownNotGeneric(i.ConstantValue.GetType().FullName), i.ConstantValue);
                            }

                            parameterList.Add(new CustomAttributeArgument(destType, value));
                            break;
                        }
                        case Arm64ImmediateToFieldAction armI:
                        {
                            var value = (object)armI.ImmValue;

                            if (value.GetType().FullName != destType.FullName)
                                value = Utils.Utils.CoerceValue(value, destType);

                            if (destType.FullName == "System.Object")
                            {
                                //Need to wrap value in another CustomAttributeArgument of the pre-casting type.
                                value = new CustomAttributeArgument(Utils.Utils.TryLookupTypeDefKnownNotGeneric(armI.ImmValue.GetType().FullName), armI.ImmValue);
                            }

                            parameterList.Add(new CustomAttributeArgument(destType, value));
                            break;
                        }
                        case RegToFieldAction { ValueRead: { } } r:
                            parameterList.Add(CoerceAnalyzedOpToParameter(r.ValueRead!, parameter));
                            break;
                        case Arm64RegisterToFieldAction { SourceOperand: { } } armR:
                        {
                            parameterList.Add(CoerceAnalyzedOpToParameter(armR.SourceOperand!, parameter));
                            break;
                        }
                        default:
                            return null;
                    }
                }

                return (potentialCtor, parameterList);
            }

            return null;
        }

        private static CustomAttribute GenerateCustomAttributeFromHardWayResult(MethodDefinition constructor, List<CustomAttributeArgument> constructorArgs, ModuleDefinition module)
        {
            var customAttribute = new CustomAttribute(module.ImportReference(constructor));

            if (constructorArgs.Count == 0)
                return customAttribute;

            if (constructor.Parameters.Count != constructorArgs.Count)
                throw new Exception("Mismatch between constructor param count & actual args count? Probably because named args support not implemented");

            foreach (var arg in constructorArgs)
                customAttribute.ConstructorArguments.Add(arg);

            return customAttribute;
        }

        private struct FieldToParameterMapping
        {
            public FieldDefinition Field;
            public ParameterDefinition Parameter;

            public FieldToParameterMapping(FieldDefinition field, ParameterDefinition parameter)
            {
                Field = field;
                Parameter = parameter;
            }
        }
    }
}