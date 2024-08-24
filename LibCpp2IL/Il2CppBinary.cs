using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;

namespace LibCpp2IL;

public abstract class Il2CppBinary(MemoryStream input) : ClassReadingBinaryReader(input)
{
    public delegate void RegistrationStructLocationFailureHandler(Il2CppBinary binary, Il2CppMetadata metadata, ref Il2CppCodeRegistration? codeReg, ref Il2CppMetadataRegistration? metaReg);

    public static event RegistrationStructLocationFailureHandler? OnRegistrationStructLocationFailure;

    protected const long VirtToRawInvalidNoMatch = long.MinValue + 1000;
    protected const long VirtToRawInvalidOutOfBounds = long.MinValue + 1001;

    public InstructionSetId InstructionSetId = null!;
    public readonly Dictionary<Il2CppMethodDefinition, List<Cpp2IlMethodRef>> ConcreteGenericMethods = new();
    public readonly Dictionary<ulong, List<Cpp2IlMethodRef>> ConcreteGenericImplementationsByAddress = new();
    public ulong[] TypeDefinitionSizePointers = [];

    private readonly long _maxMetadataUsages = LibCpp2IlMain.TheMetadata!.GetMaxMetadataUsages();
    private Il2CppMetadataRegistration _metadataRegistration = null!;
    private Il2CppCodeRegistration _codeRegistration = null!;

    private ulong[] _methodPointers = [];

    private ulong[] _genericMethodPointers = [];

    // private ulong[] _invokerPointers = Array.Empty<ulong>();
    private ulong[]? _customAttributeGenerators = []; //Pre-27 only
    private long[] _fieldOffsets = [];
    private ulong[] _metadataUsages = []; //Pre-27 only
    private ulong[][] _codeGenModuleMethodPointers = []; //24.2+

    private Il2CppType[] _types = [];
    private Il2CppGenericMethodFunctionsDefinitions[] _genericMethodTables = [];
    private Il2CppGenericInst[] _genericInsts = [];
    private Il2CppMethodSpec[] _methodSpecs = [];
    private Il2CppCodeGenModule[] _codeGenModules = []; //24.2+
    private Il2CppTokenRangePair[][] _codegenModuleRgctxRanges = [];
    private Il2CppRGCTXDefinition[][] _codegenModuleRgctxs = [];

    private Dictionary<string, Il2CppCodeGenModule> _codeGenModulesByName = new(); //24.2+
    private Dictionary<int, ulong> _genericMethodDictionary = new();
    private readonly Dictionary<ulong, Il2CppType> _typesByAddress = new();

    public abstract long RawLength { get; }
    public int NumTypes => _types.Length;

    public Il2CppType[] AllTypes => _types;

    /// <summary>
    /// Can be overriden if, like the wasm format, your data has to be unpacked and you need to use a different reader
    /// </summary>
    public virtual ClassReadingBinaryReader Reader => this;

    public int InBinaryMetadataSize { get; private set; }

    public void Init(ulong pCodeRegistration, ulong pMetadataRegistration)
    {
        var cr = pCodeRegistration > 0 ? ReadReadableAtVirtualAddress<Il2CppCodeRegistration>(pCodeRegistration) : null;
        var mr = pMetadataRegistration > 0 ? ReadReadableAtVirtualAddress<Il2CppMetadataRegistration>(pMetadataRegistration) : null;

        if (cr == null || mr == null)
        {
            LibLogger.WarnNewline("At least one of the registration structs was not able to be found. Attempting to use fallback locator delegate to find them (this will fail unless you have a plugin that helps with this!)...");
            OnRegistrationStructLocationFailure?.Invoke(this, LibCpp2IlMain.TheMetadata!, ref cr, ref mr);
            LibLogger.VerboseNewline($"After fallback, code registration is {(cr == null ? "null" : "not null")} and metadata registration is {(mr == null ? "null" : "not null")}.");
        }

        if (cr == null || mr == null)
            throw new("Failed to find code registration or metadata registration!");

        _codeRegistration = cr;
        _metadataRegistration = mr;

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        LibLogger.Verbose("\tReading generic instances...");
        var start = DateTime.Now;
        _genericInsts = Array.ConvertAll(ReadNUintArrayAtVirtualAddress(_metadataRegistration.genericInsts, _metadataRegistration.genericInstsCount), ReadReadableAtVirtualAddress<Il2CppGenericInst>);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        if (_codeRegistration.genericMethodPointers != 0)
        {
            LibLogger.Verbose("\tReading generic method pointers...");
            start = DateTime.Now;
            _genericMethodPointers = ReadNUintArrayAtVirtualAddress(_codeRegistration.genericMethodPointers, (long)_codeRegistration.genericMethodPointersCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }
        else
        {
            LibLogger.WarnNewline("\tPointer to generic method array in CodeReg is null! This isn't inherently going to cause dumping to fail but there will be no generic method data in the dump.");
            _genericMethodPointers = [];
        }

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        // These aren't actually used right now, and if we have a limited code reg (e.g. heavily inlined linux games) we can't read them anyway
        // LibLogger.Verbose("\tReading invoker pointers...");
        // start = DateTime.Now;
        // _invokerPointers = ReadNUintArrayAtVirtualAddress(_codeRegistration.invokerPointers, (long)_codeRegistration.invokerPointersCount);
        // LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        if (LibCpp2IlMain.MetadataVersion < 27)
        {
            LibLogger.Verbose("\tReading custom attribute generators...");
            start = DateTime.Now;
            _customAttributeGenerators = ReadNUintArrayAtVirtualAddress(_codeRegistration.customAttributeGeneratorListAddress, (long)_codeRegistration.customAttributeCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        LibLogger.Verbose("\tReading field offsets...");
        start = DateTime.Now;
        _fieldOffsets = ReadClassArrayAtVirtualAddress<long>(_metadataRegistration.fieldOffsetListAddress, _metadataRegistration.fieldOffsetsCount);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        LibLogger.Verbose("\tReading types...");
        start = DateTime.Now;
        var typePtrs = ReadNUintArrayAtVirtualAddress(_metadataRegistration.typeAddressListAddress, _metadataRegistration.numTypes);
        _types = new Il2CppType[_metadataRegistration.numTypes];
        for (var i = 0; i < _metadataRegistration.numTypes; ++i)
        {
            _types[i] = ReadReadableAtVirtualAddress<Il2CppType>(typePtrs[i]);
            _typesByAddress[typePtrs[i]] = _types[i];
        }

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        LibLogger.Verbose("\tReading type definition sizes...");
        start = DateTime.Now;
        TypeDefinitionSizePointers = ReadNUintArrayAtVirtualAddress(_metadataRegistration.typeDefinitionsSizes, _metadataRegistration.typeDefinitionsSizesCount);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        if (_metadataRegistration.metadataUsages != 0)
        {
            //Empty in v27
            LibLogger.Verbose("\tReading metadata usages...");
            start = DateTime.Now;
            _metadataUsages = ReadNUintArrayAtVirtualAddress(_metadataRegistration.metadataUsages, (long)Math.Max((decimal)_metadataRegistration.metadataUsagesCount, _maxMetadataUsages));
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        if (LibCpp2IlMain.MetadataVersion >= 24.2f)
        {
            LibLogger.VerboseNewline("\tReading code gen modules...");
            start = DateTime.Now;

            var codeGenModulePtrs = ReadNUintArrayAtVirtualAddress(_codeRegistration.addrCodeGenModulePtrs, (long)_codeRegistration.codeGenModulesCount);
            _codeGenModules = new Il2CppCodeGenModule[codeGenModulePtrs.Length];
            _codeGenModuleMethodPointers = new ulong[codeGenModulePtrs.Length][];
            _codegenModuleRgctxRanges = new Il2CppTokenRangePair[codeGenModulePtrs.Length][];
            _codegenModuleRgctxs = new Il2CppRGCTXDefinition[codeGenModulePtrs.Length][];
            for (var i = 0; i < codeGenModulePtrs.Length; i++)
            {
                var codeGenModule = ReadReadableAtVirtualAddress<Il2CppCodeGenModule>(codeGenModulePtrs[i]);
                _codeGenModules[i] = codeGenModule;
                _codeGenModulesByName[codeGenModule.Name] = codeGenModule;
                var name = codeGenModule.Name;
                LibLogger.VerboseNewline($"\t\t-Read module data for {name}, contains {codeGenModule.methodPointerCount} method pointers starting at 0x{codeGenModule.methodPointers:X}");
                if (codeGenModule.methodPointerCount > 0)
                {
                    try
                    {
                        var ptrs = ReadNUintArrayAtVirtualAddress(codeGenModule.methodPointers, codeGenModule.methodPointerCount);
                        _codeGenModuleMethodPointers[i] = ptrs;
                        LibLogger.VerboseNewline($"\t\t\t-Read {codeGenModule.methodPointerCount} method pointers.");
                    }
                    catch (Exception e)
                    {
                        LibLogger.VerboseNewline($"\t\t\tWARNING: Unable to get function pointers for {name}: {e.Message}");
                        _codeGenModuleMethodPointers[i] = new ulong[codeGenModule.methodPointerCount];
                    }
                }

                if (codeGenModule.rgctxRangesCount > 0)
                {
                    try
                    {
                        var ranges = ReadReadableArrayAtVirtualAddress<Il2CppTokenRangePair>(codeGenModule.pRgctxRanges, codeGenModule.rgctxRangesCount);
                        _codegenModuleRgctxRanges[i] = ranges;
                        LibLogger.VerboseNewline($"\t\t\t-Read {codeGenModule.rgctxRangesCount} RGCTX ranges.");
                    }
                    catch (Exception e)
                    {
                        LibLogger.VerboseNewline($"\t\t\tWARNING: Unable to get RGCTX ranges for {name}: {e.Message}");
                        _codegenModuleRgctxRanges[i] = new Il2CppTokenRangePair[codeGenModule.rgctxRangesCount];
                    }
                }

                if (codeGenModule.rgctxsCount > 0)
                {
                    try
                    {
                        var rgctxs = ReadReadableArrayAtVirtualAddress<Il2CppRGCTXDefinition>(codeGenModule.rgctxs, codeGenModule.rgctxsCount);
                        _codegenModuleRgctxs[i] = rgctxs;
                        LibLogger.VerboseNewline($"\t\t\t-Read {codeGenModule.rgctxsCount} RGCTXs.");
                    }
                    catch (Exception e)
                    {
                        LibLogger.VerboseNewline($"\t\t\tWARNING: Unable to get RGCTXs for {name}: {e.Message}");
                        _codegenModuleRgctxs[i] = new Il2CppRGCTXDefinition[codeGenModule.rgctxsCount];
                    }
                }
            }

            LibLogger.VerboseNewline($"\tOK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }
        else
        {
            LibLogger.Verbose("\tReading method pointers...");
            start = DateTime.Now;
            _methodPointers = ReadNUintArrayAtVirtualAddress(_codeRegistration.methodPointers, (long)_codeRegistration.methodPointersCount);
            LibLogger.VerboseNewline($"Read {_methodPointers.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        LibLogger.Verbose("\tReading generic method tables...");
        start = DateTime.Now;
        _genericMethodTables = ReadReadableArrayAtVirtualAddress<Il2CppGenericMethodFunctionsDefinitions>(_metadataRegistration.genericMethodTable, _metadataRegistration.genericMethodTableCount);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        LibLogger.Verbose("\tReading method specifications...");
        start = DateTime.Now;
        _methodSpecs = ReadReadableArrayAtVirtualAddress<Il2CppMethodSpec>(_metadataRegistration.methodSpecs, _metadataRegistration.methodSpecsCount);
        LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

        InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();

        if (_genericMethodPointers.Length > 0)
        {
            LibLogger.Verbose("\tReading generic methods...");
            start = DateTime.Now;
            _genericMethodDictionary = new Dictionary<int, ulong>();
            foreach (var table in _genericMethodTables)
            {
                var genericMethodIndex = table.GenericMethodIndex;
                var genericMethodPointerIndex = table.Indices.methodIndex;

                var methodDefIndex = GetGenericMethodFromIndex(genericMethodIndex, genericMethodPointerIndex);

                if (!_genericMethodDictionary.ContainsKey(methodDefIndex) && genericMethodPointerIndex < _genericMethodPointers.Length)
                {
                    _genericMethodDictionary.TryAdd(methodDefIndex, _genericMethodPointers[genericMethodPointerIndex]);
                }
            }

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            InBinaryMetadataSize += GetNumBytesReadSinceLastCallAndClear();
        }
        else
        {
            LibLogger.WarnNewline("\tNo generic method pointer data found, skipping generic mapping.");
        }

        _hasFinishedInitialRead = true;
    }

    private int GetGenericMethodFromIndex(int genericMethodIndex, int genericMethodPointerIndex)
    {
        Cpp2IlMethodRef? genericMethodRef;
        var methodSpec = GetMethodSpec(genericMethodIndex);
        var methodDefIndex = methodSpec.methodDefinitionIndex;
        genericMethodRef = new Cpp2IlMethodRef(methodSpec);

        if (genericMethodPointerIndex >= 0)
        {
            if (genericMethodPointerIndex < _genericMethodPointers.Length)
                genericMethodRef.GenericVariantPtr = _genericMethodPointers[genericMethodPointerIndex];
        }

        if (!ConcreteGenericMethods.ContainsKey(genericMethodRef.BaseMethod))
            ConcreteGenericMethods[genericMethodRef.BaseMethod] = [];

        ConcreteGenericMethods[genericMethodRef.BaseMethod].Add(genericMethodRef);

        if (genericMethodRef.GenericVariantPtr > 0)
        {
            if (!ConcreteGenericImplementationsByAddress.ContainsKey(genericMethodRef.GenericVariantPtr))
                ConcreteGenericImplementationsByAddress[genericMethodRef.GenericVariantPtr] = [];

            ConcreteGenericImplementationsByAddress[genericMethodRef.GenericVariantPtr].Add(genericMethodRef);
        }

        return methodDefIndex;
    }

    public abstract byte GetByteAtRawAddress(ulong addr);
    public abstract long MapVirtualAddressToRaw(ulong uiAddr, bool throwOnError = true);
    public abstract ulong MapRawAddressToVirtual(uint offset);
    public abstract ulong GetRva(ulong pointer);

    public bool TryMapRawAddressToVirtual(in uint offset, out ulong va)
    {
        try
        {
            va = MapRawAddressToVirtual(offset);
            return true;
        }
        catch (Exception)
        {
            va = 0;
            return false;
        }
    }

    public bool TryMapVirtualAddressToRaw(ulong virtAddr, out long result)
    {
        result = MapVirtualAddressToRaw(virtAddr, false);

        if (result != VirtToRawInvalidNoMatch)
            return true;

        result = 0;
        return false;
    }

    public T[] ReadClassArrayAtVirtualAddress<T>(ulong addr, long count) where T : new() => Reader.ReadClassArrayAtRawAddr<T>(MapVirtualAddressToRaw(addr), count);

    public T[] ReadReadableArrayAtVirtualAddress<T>(ulong va, long count) where T : ReadableClass, new() => Reader.ReadReadableArrayAtRawAddr<T>(MapVirtualAddressToRaw(va), count);

    public T ReadReadableAtVirtualAddress<T>(ulong va) where T : ReadableClass, new() => Reader.ReadReadable<T>(MapVirtualAddressToRaw(va));

    public ulong[] ReadNUintArrayAtVirtualAddress(ulong addr, long count) => Reader.ReadNUintArrayAtRawAddress(MapVirtualAddressToRaw(addr), (int)count);

    public override long ReadNInt() => is32Bit ? Reader.ReadInt32() : Reader.ReadInt64();

    public override ulong ReadNUint() => is32Bit ? Reader.ReadUInt32() : Reader.ReadUInt64();

    public ulong ReadPointerAtVirtualAddress(ulong addr)
    {
        return Reader.ReadNUintAtRawAddress(MapVirtualAddressToRaw(addr));
    }

    public Il2CppGenericInst GetGenericInst(int index) => _genericInsts[index];

    public Il2CppMethodSpec[] AllGenericMethodSpecs => _methodSpecs;

    public Il2CppMethodSpec GetMethodSpec(int index) => index >= _methodSpecs.Length
        ? throw new ArgumentException($"GetMethodSpec: index {index} >= length {_methodSpecs.Length}")
        : index < 0
            ? throw new ArgumentException($"GetMethodSpec: index {index} < 0")
            : _methodSpecs[index];

    public Il2CppType GetType(int index) => _types[index];
    public ulong GetRawMetadataUsage(uint index) => _metadataUsages[index];
    public ulong[] GetCodegenModuleMethodPointers(int codegenModuleIndex) => _codeGenModuleMethodPointers[codegenModuleIndex];
    public Il2CppCodeGenModule? GetCodegenModuleByName(string name) => _codeGenModulesByName[name];
    public int GetCodegenModuleIndex(Il2CppCodeGenModule module) => Array.IndexOf(_codeGenModules, module);
    public int GetCodegenModuleIndexByName(string name) => GetCodegenModuleByName(name) is { } module ? GetCodegenModuleIndex(module) : -1;
    public Il2CppTokenRangePair[] GetRgctxRangePairsForModule(Il2CppCodeGenModule module) => _codegenModuleRgctxRanges[GetCodegenModuleIndex(module)];
    public Il2CppRGCTXDefinition[] GetRgctxDataForPair(Il2CppCodeGenModule module, Il2CppTokenRangePair rangePair) => _codegenModuleRgctxs[GetCodegenModuleIndex(module)].Skip(rangePair.start).Take(rangePair.length).ToArray();

    public Il2CppType GetIl2CppTypeFromPointer(ulong pointer)
        => _typesByAddress[pointer];

    public int GetFieldOffsetFromIndex(int typeIndex, int fieldIndexInType, int fieldIndex, bool isValueType, bool isStatic)
    {
        try
        {
            var offset = -1;
            if (LibCpp2IlMain.MetadataVersion > 21)
            {
                var ptr = (ulong)_fieldOffsets[typeIndex];
                if (ptr > 0)
                {
                    var offsetOffset = (ulong)MapVirtualAddressToRaw(ptr) + 4ul * (ulong)fieldIndexInType;
                    Position = (long)offsetOffset;
                    offset = (int)ReadPrimitive(typeof(int))!; //Read 4 bytes. We can't just use ReadInt32 because that breaks e.g. Wasm. Hoping the JIT can optimize this as it knows the type.
                }
            }
            else
            {
                offset = (int)_fieldOffsets[fieldIndex];
            }

            if (offset > 0)
            {
                if (isValueType && !isStatic)
                {
                    if (is32Bit)
                    {
                        offset -= 8;
                    }
                    else
                    {
                        offset -= 16;
                    }
                }
            }

            return offset;
        }
        catch
        {
            return -1;
        }
    }

    public ulong GetMethodPointer(int methodIndex, int methodDefinitionIndex, int imageIndex, uint methodToken)
    {
        if (LibCpp2IlMain.MetadataVersion >= 24.2f)
        {
            if (_genericMethodDictionary.TryGetValue(methodDefinitionIndex, out var methodPointer))
            {
                return methodPointer;
            }

            var ptrs = _codeGenModuleMethodPointers[imageIndex];
            var methodPointerIndex = methodToken & 0x00FFFFFFu;
            return ptrs[methodPointerIndex - 1];
        }
        else
        {
            if (methodIndex >= 0)
            {
                return _methodPointers[methodIndex];
            }

            _genericMethodDictionary.TryGetValue(methodDefinitionIndex, out var methodPointer);
            return methodPointer;
        }
    }

    public ulong GetCustomAttributeGenerator(int index) => _customAttributeGenerators![index];

    public ulong[] AllCustomAttributeGenerators => LibCpp2IlMain.MetadataVersion >= 29 ? [] : LibCpp2IlMain.MetadataVersion >= 27 ? AllCustomAttributeGeneratorsV27 : _customAttributeGenerators!;

    private ulong[] AllCustomAttributeGeneratorsV27 =>
        LibCpp2IlMain.TheMetadata!.imageDefinitions
            .Select(i => (image: i, cgm: GetCodegenModuleByName(i.Name!)!))
            .SelectMany(tuple => LibCpp2ILUtils.Range(0, (int)tuple.image.customAttributeCount).Select(o => tuple.cgm.customAttributeCacheGenerator + (ulong)o * PointerSize))
            .Select(ReadPointerAtVirtualAddress)
            .ToArray();

    public abstract byte[] GetRawBinaryContent();
    public abstract ulong GetVirtualAddressOfExportedFunctionByName(string toFind);
    public virtual bool IsExportedFunction(ulong addr) => false;

    public virtual bool TryGetExportedFunctionName(ulong addr, [NotNullWhen(true)] out string? name)
    {
        name = null;
        return false;
    }

    public abstract byte[] GetEntirePrimaryExecutableSection();

    public abstract ulong GetVirtualAddressOfPrimaryExecutableSection();

    public virtual (ulong pCodeRegistration, ulong pMetadataRegistration) FindCodeAndMetadataReg(int methodCount, int typeDefinitionsCount)
    {
        LibLogger.VerboseNewline("\tAttempting to locate code and metadata registration functions...");

        var plusSearch = new BinarySearcher(this, methodCount, typeDefinitionsCount);

        LibLogger.VerboseNewline("\t\t-Searching for MetadataReg...");

        var pMetadataRegistration = LibCpp2IlMain.MetadataVersion < 24.5f
            ? plusSearch.FindMetadataRegistrationPre24_5()
            : plusSearch.FindMetadataRegistrationPost24_5();

        LibLogger.VerboseNewline("\t\t-Searching for CodeReg...");

        ulong pCodeRegistration;
        if (LibCpp2IlMain.MetadataVersion >= 24.2f)
        {
            LibLogger.VerboseNewline("\t\t\tUsing mscorlib full-disassembly approach to get codereg, this may take a while...");
            pCodeRegistration = plusSearch.FindCodeRegistrationPost2019();
        }
        else
            pCodeRegistration = plusSearch.FindCodeRegistrationPre2019();

        if (pCodeRegistration == 0 && LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput)
        {
            LibLogger.Info("Couldn't identify a CodeRegistration address. If you know it, enter it now, otherwise enter nothing or zero to fail: ");
            var crInput = Console.ReadLine();
            ulong.TryParse(crInput, NumberStyles.HexNumber, null, out pCodeRegistration);
        }

        if (pMetadataRegistration == 0 && LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput)
        {
            LibLogger.Info("Couldn't identify a MetadataRegistration address. If you know it, enter it now, otherwise enter nothing or zero to fail: ");
            var mrInput = Console.ReadLine();
            ulong.TryParse(mrInput, NumberStyles.HexNumber, null, out pMetadataRegistration);
        }

        return (pCodeRegistration, pMetadataRegistration);
    }
}
