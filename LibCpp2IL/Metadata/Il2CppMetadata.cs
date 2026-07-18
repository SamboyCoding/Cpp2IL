using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AssetRipper.Primitives;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Metadata;

public class Il2CppMetadata : ClassReadingBinaryReader
{
    public const uint MetadataMagic = 0xFAB11BAF;
    public sealed override float MetadataVersion { get; }
    public UnityVersion UnityVersion { get; }
    
    public Il2CppGlobalMetadataHeader metadataHeader;
    public Il2CppAssemblyDefinition[] AssemblyDefinitions;
    public Il2CppImageDefinition[] imageDefinitions;
    public Il2CppTypeDefinition[] typeDefs;
    private Il2CppInterfaceOffset[] interfaceOffsets;
    public uint[] VTableMethodIndices;
    public Il2CppMethodDefinition[] methodDefs;
    private Il2CppParameterDefinition[] parameterDefs;
    internal Il2CppFieldDefinition[] fieldDefs;
    private Il2CppFieldDefaultValue[] fieldDefaultValues;
    private Il2CppParameterDefaultValue[] parameterDefaultValues;
    internal Il2CppPropertyDefinition[] propertyDefs;
    public List<Il2CppCustomAttributeTypeRange>? attributeTypeRanges; //Removed in v29
    public Il2CppStringLiteral[] stringLiterals;
    public Il2CppMetadataUsageList[]? metadataUsageLists; //Removed in v27
    private Il2CppMetadataUsagePair[]? metadataUsagePairs; //Removed in v27
    public Il2CppRGCTXDefinition[]? RgctxDefinitions; //Moved to binary in v24.2
    
    public int[]? attributeTypes; //Removed in v29
    public List<Il2CppCustomAttributeDataRange>? AttributeDataRanges; //Added in v29

    public Il2CppInlineArrayLength[]? TypeInlineArrays; //v104+. TODO: These theoretically map to [InlineArray] attributes in dummy dlls, but that's a recent language version feature, does unity actually support them yet?
    
    public Il2CppVariableWidthIndex<Il2CppType>[] interfaceIndices;

    //Moved to binary in v27.
    public Dictionary<uint, SortedDictionary<uint, uint>>? metadataUsageDic;

    private Il2CppNestedTypeIndex[] nestedTypeIndices;
    private Il2CppEventDefinition[] eventDefs;
    private Il2CppGenericContainer[] genericContainers;
    public Il2CppFieldRef[] fieldRefs;
    private Il2CppGenericParameter[] genericParameters;
    public Il2CppVariableWidthIndex<Il2CppType>[] constraintIndices;
    public int[]? exportedTypes; //Added in v24

    public int[] referencedAssemblies;

#nullable disable
    /// <summary>
    /// Set by <see cref="LibCpp2IlContextBuilder"/> after construction.
    /// </summary>
    public LibCpp2IlContext OwningContext { get; internal set; }
#nullable restore

    private readonly Dictionary<Il2CppVariableWidthIndex<Il2CppFieldDefinition>, Il2CppFieldDefaultValue> _fieldDefaultValueLookup = new();
    private readonly Dictionary<Il2CppFieldDefinition, Il2CppFieldDefaultValue> _fieldDefaultLookupNew = new();
    
    public int TypeDefinitionCount => typeDefs.Length;
    public int MethodDefinitionCount => methodDefs.Length;

    public static bool HasMetadataHeader(byte[] bytes) => bytes.Length >= 4 && BitConverter.ToUInt32(bytes, 0) == 0xFAB11BAF;
    
    internal static int GetIndexWidth(int elementCount)
    {
        return elementCount switch
        {
            <= byte.MaxValue => sizeof(byte),
            <= ushort.MaxValue => sizeof(ushort),
            _ => sizeof(int)
        };
    }

    public static Il2CppMetadata ReadFrom(byte[] bytes, UnityVersion unityVersion)
    {
        if (!HasMetadataHeader(bytes))
        {
            //Magic number is wrong
            throw new FormatException("Invalid or corrupt metadata (magic number check failed)");
        }

        var version = BitConverter.ToInt32(bytes, 4);
        if (version is < 23 or > 106)
        {
            throw new FormatException("Unsupported metadata version found! We support 23-106, got " + version);
        }

        LibLogger.VerboseNewline($"\tIL2CPP Metadata Declares its version as {version}");

        float actualVersion;
        if (version == 24)
        {
            if (unityVersion.GreaterThanOrEquals(2020, 1, 11))
                actualVersion = 24.4f; //2020.1.11-17 were released prior to 2019.4.21, so are still on 24.4
            else if (unityVersion.GreaterThanOrEquals(2020))
                actualVersion = 24.3f; //2020.1.0-10 were released prior to to 2019.4.15, so are still on 24.3
            else if (unityVersion.GreaterThanOrEquals(2019, 4, 21))
                actualVersion = 24.5f; //2019.4.21 introduces v24.5
            else if (unityVersion.GreaterThanOrEquals(2019, 4, 15))
                actualVersion = 24.4f; //2019.4.15 introduces v24.4
            else if (unityVersion.GreaterThanOrEquals(2019, 3, 7))
                actualVersion = 24.3f; //2019.3.7 introduces v24.3
            else if (unityVersion.GreaterThanOrEquals(2019))
                actualVersion = 24.2f; //2019.1.0 introduces v24.2
            else if (unityVersion.GreaterThanOrEquals(2018, 4, 34))
                actualVersion = 24.15f; //2018.4.34 made a tiny little change which just removes HashValueIndex from AssemblyNameDefinition
            else if (unityVersion.GreaterThanOrEquals(2018, 3))
                actualVersion = 24.1f; //2018.3.0 introduces v24.1
            else
                actualVersion = version; //2017.1.0 was the first v24 version
        }
        else if (version == 27)
        {
            if (unityVersion.GreaterThanOrEquals(2021, 1))
                actualVersion = 27.2f; //2021.1 and up is v27.2, which just changes Il2CppType to have one new bit
            else if (unityVersion.GreaterThanOrEquals(2020, 2, 4))
                actualVersion = 27.1f; //2020.2.4 and above is v27.1
            else
                actualVersion = version; //2020.2 and above is v27
        }
        else if (version == 29)
        {
            if (unityVersion.GreaterThanOrEquals(2022, 1, 0, UnityVersionType.Beta, 7))
                actualVersion = 29.1f; //2022.1.0b7 introduces v29.1 which adds two new pointers to codereg
            else
                actualVersion = 29; //2021.3.0 introduces v29
        }
        else if (version == 31)
        {
            //2022.3.33 introduces v31. Unity why would you bump this on a minor version.
            //Adds one new field (return type token) to method def
            //2021.3.40 backported the new field but NOT the changes from v29.1, so there's a 31.1 now.
            if (unityVersion.GreaterThanOrEquals(2022, 3, 33, UnityVersionType.Final, 1))
                //V31 with changes in codereg
                actualVersion = 31.1f;
            else
                //v31 WITHOUT changes in codereg 
                actualVersion = 31;
        }
        else if (version == 106)
        {
            //6.5.0a6 => v106
            //  - changes GenericParameterIndex, FieldIndex, DefaultValueDataIndex to variable size
            //  - changes Il2CppGenericContainer type_argc int32 => uint16, is_method int32 => uint8
            //6.6.0a6 => 106.1
            //  - adds 2 new fields to Il2CppMetadataRegistration
            //  - removes the second value of MetadataUsageType
            if(unityVersion.GreaterThanOrEquals(6000, 6, 0, UnityVersionType.Alpha, 6))
                actualVersion = 106.1f;
            else
                actualVersion = 106;
        }
        else
        {
            //6000.3 and 6000.5 alphas made a bunch of quick-succession version changes:
            //6.3.0a2 => v35
            //  - drops elementTypeIndex from Il2CppTypeDefinition
            //  - drops lengths from string literals
            //6.3.0a5 => v38
            //  - adds moduleToken to Il2CppAssemblyDefinition
            //  - adds Il2CppSectionMetadataStruct (offset/size/count) replacing all the offset/size pairs in metadata header
            //  - adds variable size (32/16/8-bit) indices into TypeDefinition array, GenericContainer array, and Type array (the one in the binary) 
            //  - other changes (related to Il2CppClass) not relevant to metadata reader
            //6.3.0b1 => v39
            //  - changes ParameterIndex to variable size, same as TypeIndex was in v38
            //6.5.0a3 => v104
            //  - adds typeInlineArrays section to metadata
            //  - Adds bit 18 to TypeDefinition bitfield
            //  - changes InterfaceIndex, EventIndex, PropertyIndex, NestedTypeIndex to variable size
            //  - other changes (some related to Il2CppClass, many seem to be porting over coreclr structures) not relevant to metadata reader
            //6.5.0a5 => v105
            //  - changes MethodIndex to variable size.
            //  - brings the Il2CppClass changes from v38 to v10x.
            actualVersion = version;
        }

        LibLogger.InfoNewline($"\tUsing actual IL2CPP Metadata version {actualVersion}");

        return new Il2CppMetadata(new MemoryStream(bytes), unityVersion, actualVersion);
    }

    private Il2CppMetadata(MemoryStream stream, UnityVersion unityVersion, float metadataVersion) : base(stream)
    {
        UnityVersion = unityVersion;
        MetadataVersion = metadataVersion;
        metadataHeader = ReadReadable<Il2CppGlobalMetadataHeader>();
        
        if (metadataHeader.magicNumber != MetadataMagic)
            throw new Exception($"ERROR: Magic number mismatch. Expecting 0x{MetadataMagic:X8} but got 0x{metadataHeader.magicNumber:X8}");

        int typeIndexSize;
        if (metadataVersion >= 38)
        {
            //Need to init dynamic widths
            
            //We're on v38 or later so we know we can use .Count on these header sections.
            var typeDefinitionIndexWidth = GetIndexWidth(metadataHeader.typeDefinitions.Count);
            var genericContainerIndexWidth = GetIndexWidth(metadataHeader.genericContainers.Count);
            
            //Unfortunately, the type list is not in the metadata file, but the binary, so we don't have its count. We do have interface offsets, though, and those are just an index and an int.
            //So we can derive the width of a type index from that.
            var bytesPerInterfaceOffset = metadataHeader.interfaceOffsets.Size / metadataHeader.interfaceOffsets.Count;
            typeIndexSize = bytesPerInterfaceOffset - sizeof(int); //Subtract the int for the offset, the rest is the type index
            
            //v39 additionally makes parameter definitions use dynamic widths
            var parameterDefinitionIndexWidth = metadataVersion >= 39 ? GetIndexWidth(metadataHeader.parameters.Count) : sizeof(int);
            
            //v104 extends dynamic widths to interface, event, property, and nested type indices
            var interfaceOffsetIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.interfaceOffsets.Count) : sizeof(int);
            var eventIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.events.Count) : sizeof(int);
            var propertyIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.properties.Count) : sizeof(int);
            var nestedTypeIndexWidth = metadataVersion >= 104 ? GetIndexWidth(metadataHeader.nestedTypes.Count) : sizeof(int);
            
            //v105 extends dynamic widths to method definitions as well
            var methodDefinitionIndexWidth = metadataVersion >= 105 ? GetIndexWidth(metadataHeader.methods.Count) : sizeof(int);
            
            //v106 extends dynamic widths to generic parameters, field indices, and default value data indices
            var genericParameterIndexWidth = metadataVersion >= 106 ? GetIndexWidth(metadataHeader.genericParameters.Count) : sizeof(int);
            var fieldIndexWidth = metadataVersion >= 106 ? GetIndexWidth(metadataHeader.fields.Count) : sizeof(int);
            var defaultValueDataIndexWidth = metadataVersion >= 106 ? GetIndexWidth(metadataHeader.fieldAndParameterDefaultValueData.Count) : sizeof(int);
            
            LibLogger.VerboseNewline($"\tDetermined variable index widths - Il2CppTypeDefinition: {typeDefinitionIndexWidth * 8} bits, Il2CppGenericContainer: {genericContainerIndexWidth * 8} bits, Il2CppType: {typeIndexSize * 8} bits, Il2CppParameterDefinition: {parameterDefinitionIndexWidth * 8} bits");
            
            if(metadataVersion >= 104)
                LibLogger.VerboseNewline($"\t...InterfaceIndex: {interfaceOffsetIndexWidth * 8} bits, EventIndex: {eventIndexWidth * 8} bits, PropertyIndex: {propertyIndexWidth * 8} bits, NestedTypeIndex: {nestedTypeIndexWidth * 8} bits");
            
            if(metadataVersion >= 105)
                LibLogger.VerboseNewline($"\t...MethodDefinitionIndex: {methodDefinitionIndexWidth * 8} bits, GenericParameterIndex: {genericParameterIndexWidth * 8} bits, FieldIndex: {fieldIndexWidth * 8} bits, DefaultValueDataIndex: {defaultValueDataIndexWidth * 8} bits");
            
            
            Il2CppVariableWidthIndex<Il2CppType>.BeginReadSession(typeIndexSize);
            Il2CppVariableWidthIndex<Il2CppTypeDefinition>.BeginReadSession(typeDefinitionIndexWidth);
            Il2CppVariableWidthIndex<Il2CppGenericContainer>.BeginReadSession(genericContainerIndexWidth);
            Il2CppVariableWidthIndex<Il2CppParameterDefinition>.BeginReadSession(parameterDefinitionIndexWidth);
            
            Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.BeginReadSession(interfaceOffsetIndexWidth);
            Il2CppVariableWidthIndex<Il2CppEventDefinition>.BeginReadSession(eventIndexWidth);
            Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.BeginReadSession(propertyIndexWidth);
            Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.BeginReadSession(nestedTypeIndexWidth);
            
            Il2CppVariableWidthIndex<Il2CppMethodDefinition>.BeginReadSession(methodDefinitionIndexWidth);
            Il2CppVariableWidthIndex<Il2CppGenericParameter>.BeginReadSession(genericParameterIndexWidth);
            Il2CppVariableWidthIndex<Il2CppFieldDefinition>.BeginReadSession(fieldIndexWidth);
            Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.BeginReadSession(defaultValueDataIndexWidth);
        }
        else
        {
            typeIndexSize = sizeof(int);
            
            LibLogger.VerboseNewline($"\tMetadata version is pre-v38, using fixed 32-bit widths for all variable width indices");
            
            Il2CppVariableWidthIndex<Il2CppType>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppTypeDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppGenericContainer>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppParameterDefinition>.BeginReadSessionOnLegacyVersion();
            
            Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppEventDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.BeginReadSessionOnLegacyVersion();
            
            Il2CppVariableWidthIndex<Il2CppMethodDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppGenericParameter>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppFieldDefinition>.BeginReadSessionOnLegacyVersion();
            Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.BeginReadSessionOnLegacyVersion();
        }

        try
        {
            LibLogger.Verbose("\tReading image definitions...");
            var start = DateTime.Now;
            imageDefinitions = ReadMetadataClassArray<Il2CppImageDefinition>(metadataHeader.images);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading assembly definitions...");
            start = DateTime.Now;
            AssemblyDefinitions = ReadMetadataClassArray<Il2CppAssemblyDefinition>(metadataHeader.assemblies);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading type definitions...");
            start = DateTime.Now;
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(metadataHeader.typeDefinitions);
            LibLogger.VerboseNewline($"{typeDefs.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading interface offsets...");
            start = DateTime.Now;
            interfaceOffsets = ReadMetadataClassArray<Il2CppInterfaceOffset>(metadataHeader.interfaceOffsets);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading vtable indices...");
            start = DateTime.Now;
            VTableMethodIndices = ReadClassArrayAtRawAddr<uint>(metadataHeader.vtableMethods.Offset, metadataHeader.vtableMethods.Size / sizeof(uint));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method definitions...");
            start = DateTime.Now;
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(metadataHeader.methods);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method parameter definitions...");
            start = DateTime.Now;
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(metadataHeader.parameters);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading field definitions...");
            start = DateTime.Now;
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(metadataHeader.fields);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading default field values...");
            start = DateTime.Now;
            fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(metadataHeader.fieldDefaultValues);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading default parameter values...");
            start = DateTime.Now;
            parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(metadataHeader.parameterDefaultValues);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading property definitions...");
            start = DateTime.Now;
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(metadataHeader.properties);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading interface definitions...");
            start = DateTime.Now;
            interfaceIndices = ReadIndexArrayAtRawAddress<Il2CppType>(metadataHeader.interfaces.Offset, metadataHeader.interfaces.Size / typeIndexSize);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading nested type definitions...");
            start = DateTime.Now;
            nestedTypeIndices = ReadMetadataClassArray<Il2CppNestedTypeIndex>(metadataHeader.nestedTypes);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading event definitions...");
            start = DateTime.Now;
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(metadataHeader.events);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic container definitions...");
            start = DateTime.Now;
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(metadataHeader.genericContainers);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic parameter definitions...");
            start = DateTime.Now;
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(metadataHeader.genericParameters);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic parameter constraint indices...");
            start = DateTime.Now;
            constraintIndices = ReadIndexArrayAtRawAddress<Il2CppType>(metadataHeader.genericParameterConstraints.Offset, metadataHeader.genericParameterConstraints.Size / sizeof(int));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading referenced assemblies...");
            start = DateTime.Now;
            referencedAssemblies = ReadClassArrayAtRawAddr<int>(metadataHeader.referencedAssemblies.Offset, metadataHeader.referencedAssemblies.Size / sizeof(int));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //v17+ fields
            LibLogger.Verbose("\tReading string definitions...");
            start = DateTime.Now;
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(metadataHeader.stringLiteral);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (MetadataVersion >= 24)
            {
                LibLogger.Verbose("\tReading exported types...");
                start = DateTime.Now;

                exportedTypes = ReadClassArrayAtRawAddr<int>(metadataHeader.exportedTypeDefinitions.Offset, metadataHeader.exportedTypeDefinitions.Size / sizeof(int));

                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            if (MetadataVersion < 24.2f)
            {
                LibLogger.Verbose("\tReading RGCTX data...");
                start = DateTime.Now;

                RgctxDefinitions = ReadMetadataClassArray<Il2CppRGCTXDefinition>(metadataHeader.rgctxEntries);

                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            //Removed in v27 (2020.2) and also 24.5 (2019.4.21)
            if (MetadataVersion < 27f)
            {
                LibLogger.Verbose("\tReading usage data...");
                start = DateTime.Now;
                metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(metadataHeader.metadataUsageLists);
                metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(metadataHeader.metadataUsagePairs);

                DecipherMetadataUsage();
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tReading field references...");
            start = DateTime.Now;
            fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(metadataHeader.fieldRefs);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            //v21+ fields

            if (MetadataVersion < 29)
            {
                //Removed in v29
                LibLogger.Verbose("\tReading attribute types...");
                start = DateTime.Now;
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(metadataHeader.attributesInfo).ToList();
                attributeTypes = ReadClassArrayAtRawAddr<int>(metadataHeader.attributeTypes.Offset, metadataHeader.attributeTypes.Size / sizeof(int));
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                //Since v29
                LibLogger.Verbose("\tReading Attribute data...");
                start = DateTime.Now;

                //Pointer array
                AttributeDataRanges = ReadReadableArrayAtRawAddr<Il2CppCustomAttributeDataRange>(metadataHeader.attributeDataRange.Offset, metadataHeader.attributeDataRange.Size / 8).ToList();
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            
            //v104+ fields
            if(MetadataVersion >= 104)
            {
                LibLogger.Verbose("\tReading type inline arrays...");
                start = DateTime.Now;

                TypeInlineArrays = ReadMetadataClassArray<Il2CppInlineArrayLength>(metadataHeader.typeInlineArrays);
                
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tBuilding Lookup Table for field defaults...");
            start = DateTime.Now;
            for (var i = 0; i < fieldDefaultValues.Length; i++)
            {
                if(MetadataVersion >= 104 && i == fieldDefaultValues.Length - 1 && fieldDefaultValues[i].fieldIndex.Value == -1)
                    //v104 added this silly dummy entry at the end with field and type index -1. We skip it.
                    continue;
                
                var il2CppFieldDefaultValue = fieldDefaultValues[i];
                _fieldDefaultValueLookup[il2CppFieldDefaultValue.fieldIndex] = il2CppFieldDefaultValue;
                _fieldDefaultLookupNew[GetFieldDefinitionFromIndex(il2CppFieldDefaultValue.fieldIndex)] = il2CppFieldDefaultValue;
            }

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            _hasFinishedInitialRead = true;
        }
        finally
        {
            Il2CppVariableWidthIndex<Il2CppType>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppTypeDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppGenericContainer>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppParameterDefinition>.EndReadSession();
            
            Il2CppVariableWidthIndex<Il2CppInterfaceOffset>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppEventDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppPropertyDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppNestedTypeIndex>.EndReadSession();
            
            Il2CppVariableWidthIndex<Il2CppMethodDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppGenericParameter>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppFieldDefinition>.EndReadSession();
            Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.EndReadSession();
        }
    }
#pragma warning restore 8618

    internal void SetOwningContext(LibCpp2IlContext context)
    {
        OwningContext = context;

        SetOwningContext(imageDefinitions, context);
        SetOwningContext(AssemblyDefinitions, context);
        SetOwningContext(typeDefs, context);
        SetOwningContext(interfaceOffsets, context);
        SetOwningContext(methodDefs, context);
        SetOwningContext(parameterDefs, context);
        SetOwningContext(fieldDefs, context);
        SetOwningContext(fieldDefaultValues, context);
        SetOwningContext(parameterDefaultValues, context);
        SetOwningContext(propertyDefs, context);
        SetOwningContext(eventDefs, context);
        SetOwningContext(genericContainers, context);
        SetOwningContext(genericParameters, context);
        SetOwningContext(stringLiterals, context);
        SetOwningContext(fieldRefs, context);

        if (RgctxDefinitions != null)
            SetOwningContext(RgctxDefinitions, context);

        // Set on sub-objects not directly in arrays
        foreach (var asm in AssemblyDefinitions)
            asm.AssemblyName.OwningContext = context;
    }

    private static void SetOwningContext<T>(T[] items, LibCpp2IlContext context) where T : ReadableClass
    {
        foreach (var item in items)
            item.OwningContext = context;
    }

    private T[] ReadMetadataClassArray<T>(Il2CppGlobalMetadataSectionHeader section) where T : ReadableClass, new()
    {
        //First things first, we're going to be moving the position around a lot, so we need to lock. 
        GetLockOrThrow();

        Position = section.Offset;

        try
        {
            //Length is in bytes, not in elements, so we need to work out the element size to know how big of an array to allocate.
            //We do this by reading the first element, then count how many bytes we read.
            var first = ReadReadableHereNoLock<T>();

            //How many bytes did we read?
            var elementSize = (int)(Position - section.Offset);

            //For build report purposes, we track that many bytes. FillReadableArrayHereNoLock will add the rest.
            TrackRead<T>(elementSize);

            //Now we can work out how many elements there are.
            var numElements = section.Size / elementSize;
            
            if (numElements == 0) {
                return [];
            }
            
            Debug.Assert(!section.HasCount || section.Count == numElements, $"Section {typeof(T).Name} has a count field of {section.Count} but we calculated {numElements} elements based on the size and element size. Dynamic-width indices wrong?");

            //And so we can allocate an array of that length, and assign the first element.
            var arr = new T[numElements];
            arr[0] = first;

            //And finally, read the rest of the elements, starting at index 1.
            FillReadableArrayHereNoLock(arr, 1);

            return arr;
        }
        finally
        {
            ReleaseLock();
        }
    }

    private void DecipherMetadataUsage()
    {
        if(metadataUsageLists == null || metadataUsagePairs == null)
            throw new InvalidOperationException("Called DecipherMetadataUsage on v27 or newer metadata");
        
        metadataUsageDic = new();
        for (var i = 1u; i <= 6u; i++)
        {
            metadataUsageDic[i] = new();
        }

        foreach (var metadataUsageList in metadataUsageLists)
        {
            for (var i = 0; i < metadataUsageList.count; i++)
            {
                var offset = metadataUsageList.start + i;
                var metadataUsagePair = metadataUsagePairs[offset];
                var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                metadataUsageDic[usage][metadataUsagePair.destinationIndex] = decodedIndex;
            }
        }
    }

    public uint GetMaxMetadataUsages()
    {
        if (metadataUsageDic == null)
            //V27+
            return 0;

        return metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
    }

    private uint GetEncodedIndexType(uint index)
    {
        return (index & 0xE0000000) >> 29;
    }

    private uint GetDecodedMethodIndex(uint index)
    {
        return index & 0x1FFFFFFFU;
    }

    //Getters for human readability
    public Il2CppFieldDefaultValue? GetFieldDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppFieldDefinition> index)
    {
        return _fieldDefaultValueLookup.GetOrDefault(index);
    }

    public Il2CppFieldDefaultValue? GetFieldDefaultValue(Il2CppFieldDefinition field)
    {
        return _fieldDefaultLookupNew.GetOrDefault(field);
    }

    public (Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> ptr, Il2CppVariableWidthIndex<Il2CppType> type) GetFieldDefaultValue(Il2CppVariableWidthIndex<Il2CppFieldDefinition> fieldIdx)
    {
        var fieldDef = GetFieldDefinitionFromIndex(fieldIdx);
        var fieldType = OwningContext.Binary.GetType(fieldDef.typeIndex);
        if ((fieldType.Attrs & (int)FieldAttributes.HasFieldRVA) != 0)
        {
            var fieldDefault = GetFieldDefaultValueFromIndex(fieldIdx);

            if (fieldDefault == null)
                return (Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.Null, Il2CppVariableWidthIndex<Il2CppType>.Null);

            return (ptr: fieldDefault.dataIndex, type: fieldDefault.typeIndex);
        }

        return (Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy>.Null, Il2CppVariableWidthIndex<Il2CppType>.Null);
    }

    public Il2CppParameterDefaultValue? GetParameterDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition> index)
    {
        return parameterDefaultValues.FirstOrDefault(x => x.parameterIndex == index);
    }

    public int GetDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppDefaultValueDataDummy> index)
    {
        return metadataHeader.fieldAndParameterDefaultValueData.Offset + index.Value;
    }

    /// <summary>
    /// Read a byte array from the string data section of the metadata.
    /// </summary>
    /// <param name="index">The offset relative to the start of the string section.</param>
    /// <returns>The </returns>
    public byte[] GetByteArrayFromIndex(int index)
    {
        var offset = metadataHeader.@string.Offset + index;
        var count = ReadUnityCompressedUIntAtRawAddr(offset, out var bytesRead);
        return ReadByteArrayAtRawAddress(offset + bytesRead, (int)count);
    }

    private ConcurrentDictionary<int, string> _cachedStrings = new ConcurrentDictionary<int, string>();

    public string GetStringFromIndex(int index)
    {
        GetLockOrThrow();
        try
        {
            return ReadStringFromIndexNoReadLock(index);
        }
        finally
        {
            ReleaseLock();
        }
    }

    internal string ReadStringFromIndexNoReadLock(int index)
    {
        if (!_cachedStrings.ContainsKey(index))
            _cachedStrings[index] = ReadStringToNullNoLock(metadataHeader.@string.Offset + index);
        return _cachedStrings[index];
    }

    public Il2CppCustomAttributeTypeRange? GetCustomAttributeData(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, out int idx)
    {
        if(MetadataVersion >= 29f)
            throw new("This method is not valid for metadata versions 29 and above");
        
        idx = -1;

        if (MetadataVersion <= 24f)
        {
            idx = customAttributeIndex;
            return attributeTypeRanges![customAttributeIndex]; //Not-null assertion because we've checked version
        }

        var target = new Il2CppCustomAttributeTypeRange { token = token };

        if (imageDef.customAttributeStart < 0)
            throw new("Image has customAttributeStart < 0");
        if (imageDef.customAttributeStart + imageDef.customAttributeCount > attributeTypeRanges!.Count) //Not-null assertion because we've checked version is < 29
            throw new($"Image has customAttributeStart + customAttributeCount > attributeTypeRanges.Count ({imageDef.customAttributeStart + imageDef.customAttributeCount} > {attributeTypeRanges.Count})");

        idx = attributeTypeRanges.BinarySearch(imageDef.customAttributeStart, (int)imageDef.customAttributeCount, target, new TokenComparer());

        return idx < 0 ? null : attributeTypeRanges[idx];
    }
    
    public Il2CppTypeDefinition GetTypeDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppTypeDefinition> index) => typeDefs[index.Value];

    public Il2CppTypeDefinition GetExportedTypeDefintionFromIndex(int index) => typeDefs[exportedTypes![index]];
    
    public Il2CppGenericContainer GetGenericContainerFromIndex(Il2CppVariableWidthIndex<Il2CppGenericContainer> index) => genericContainers[index.Value];
    
    public Il2CppParameterDefinition GetParameterDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition> index) => parameterDefs[index.Value];
    
    public Il2CppNestedTypeIndex GetNestedTypeIndicesFromIndex(Il2CppVariableWidthIndex<Il2CppNestedTypeIndex> index) => nestedTypeIndices[index.Value];

    public Il2CppNestedTypeIndex GetNestedTypeIndicesFromOffset(Il2CppVariableWidthIndex<Il2CppNestedTypeIndex> startIndex, ushort offset) => nestedTypeIndices[startIndex.Value + offset];

    public IEnumerable<int> GetNestedTypeIndicesFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppNestedTypeIndex> index, int count) => nestedTypeIndices.Skip(index.Value).Take(count).Select(n => n.Value);

    public Il2CppVariableWidthIndex<Il2CppType> GetInterfaceIndicesFromIndex(Il2CppVariableWidthIndex<Il2CppInterfaceOffset> index) => interfaceIndices[index.Value];

    public Il2CppVariableWidthIndex<Il2CppType> GetInterfaceIndicesFromOffset(Il2CppVariableWidthIndex<Il2CppInterfaceOffset> startIndex, ushort offset) => interfaceIndices[startIndex.Value + offset];
    
    public IEnumerable<Il2CppVariableWidthIndex<Il2CppType>> GetInterfaceIndicesFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppInterfaceOffset> index, int count) => interfaceIndices.Skip(index.Value).Take(count);

    public Il2CppInterfaceOffset[] GetInterfaceOffsetsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppInterfaceOffset> index, int count) => interfaceOffsets.SubArray(index.Value, count);
    
    public Il2CppPropertyDefinition GetPropertyDefinitionsFromIndex(Il2CppVariableWidthIndex<Il2CppPropertyDefinition> index) => propertyDefs[index.Value];

    public Il2CppPropertyDefinition GetPropertyDefinitionsFromOffset(Il2CppVariableWidthIndex<Il2CppPropertyDefinition> startIndex, ushort offset) => propertyDefs[startIndex.Value + offset];
    
    public Il2CppPropertyDefinition[] GetPropertyDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppPropertyDefinition> index, int count) => propertyDefs.SubArray(index.Value, count);
    
    public Il2CppEventDefinition[] GetEventDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppEventDefinition> index, int count) => eventDefs.SubArray(index.Value, count);
    
    public Il2CppMethodDefinition GetMethodDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppMethodDefinition> index) => methodDefs[index.Value];

    public Il2CppMethodDefinition GetMethodDefinitionFromOffset(Il2CppVariableWidthIndex<Il2CppMethodDefinition> startIndex, ushort offset) => methodDefs[startIndex.Value + offset];
    
    public Il2CppMethodDefinition[] GetMethodDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppMethodDefinition> index, int count) => methodDefs.SubArray(index.Value, count);
    
    public Il2CppGenericParameter GetGenericParameterFromIndex(Il2CppVariableWidthIndex<Il2CppGenericParameter> index) => genericParameters[index.Value];
    
    public Il2CppFieldDefinition GetFieldDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppFieldDefinition> index) => fieldDefs[index.Value];

    public Il2CppFieldDefinition GetFieldDefinitionFromOffset(Il2CppVariableWidthIndex<Il2CppFieldDefinition> startIndex, ushort offset) => fieldDefs[startIndex.Value + offset];

    public Il2CppFieldDefinition[] GetFieldDefinitionsFromIndexAndCount(Il2CppVariableWidthIndex<Il2CppFieldDefinition> index, int count) => fieldDefs.SubArray(index.Value, count);

    public string GetStringLiteralFromIndex(uint index)
    {
        var stringLiteral = stringLiterals[index];
        
        if(MetadataVersion < 35f)
            return Encoding.UTF8.GetString(ReadByteArrayAtRawAddress(metadataHeader.stringLiteralData.Offset + stringLiteral.dataIndex, (int)stringLiteral.length));
        
        //v35 and above - no length field. have to read until next string literal or end of string literal data
        var nextOffset = index < stringLiterals.Length - 1 
            ? metadataHeader.stringLiteralData.Offset + stringLiterals[index + 1].dataIndex 
            : metadataHeader.stringLiteralData.Offset + metadataHeader.stringLiteralData.Size;
        
        var startOffset = metadataHeader.stringLiteralData.Offset + stringLiteral.dataIndex;
        var length = nextOffset - startOffset;
        return Encoding.UTF8.GetString(ReadByteArrayAtRawAddress(startOffset, length));
    }
}
