﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL
{
    public abstract class Il2CppBinary : ClassReadingBinaryReader
    {
        public InstructionSet InstructionSet;
        
        protected readonly long maxMetadataUsages;
        private Il2CppMetadataRegistration metadataRegistration;
        private Il2CppCodeRegistration codeRegistration;
        protected ulong[] methodPointers;
        private ulong[] genericMethodPointers;
        private ulong[] invokerPointers;
        private ulong[]? customAttributeGenerators; //Pre-27 only
        protected long[] fieldOffsets;
        protected ulong[] metadataUsages; //Pre-27 only
        protected ulong[][] codeGenModuleMethodPointers; //24.2+
        protected Il2CppType[] types;
        private Il2CppGenericMethodFunctionsDefinitions[] genericMethodTables;
        protected Il2CppGenericInst[] genericInsts;
        protected Il2CppMethodSpec[] methodSpecs;
        protected Il2CppCodeGenModule[] codeGenModules; //24.2+
        protected Il2CppTokenRangePair[][] codegenModuleRgctxRanges;
        protected Il2CppRGCTXDefinition[][] codegenModuleRgctxs;
        protected Dictionary<int, ulong> genericMethodDictionary;
        protected readonly Dictionary<ulong, Il2CppType> typesDict = new();
        public readonly Dictionary<Il2CppMethodDefinition, List<Il2CppGenericMethodRef>> ConcreteGenericMethods = new();
        public readonly Dictionary<ulong, List<Il2CppGenericMethodRef>> ConcreteGenericImplementationsByAddress = new();
        public ulong[] TypeDefinitionSizePointers;

        protected Il2CppBinary(MemoryStream input, long maxMetadataUsages) : base(input)
        {
            this.maxMetadataUsages = maxMetadataUsages;
        }

        public abstract long RawLength { get; }
        public int NumTypes => types.Length;

        public void Init(ulong pCodeRegistration, ulong pMetadataRegistration)
        {
            codeRegistration = ReadClassAtVirtualAddress<Il2CppCodeRegistration>(pCodeRegistration);
            metadataRegistration = ReadClassAtVirtualAddress<Il2CppMetadataRegistration>(pMetadataRegistration);

            LibLogger.Verbose("\tReading generic instances...");
            var start = DateTime.Now;
            genericInsts = Array.ConvertAll(ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.genericInsts, metadataRegistration.genericInstsCount), ReadClassAtVirtualAddress<Il2CppGenericInst>);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic method pointers...");
            start = DateTime.Now;
            genericMethodPointers = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.genericMethodPointers, (long) codeRegistration.genericMethodPointersCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading invoker pointers...");
            start = DateTime.Now;
            invokerPointers = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.invokerPointers, (long) codeRegistration.invokerPointersCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (LibCpp2IlMain.MetadataVersion < 27)
            {
                LibLogger.Verbose("\tReading custom attribute generators...");
                start = DateTime.Now;
                customAttributeGenerators = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.customAttributeGeneratorListAddress, (long) codeRegistration.customAttributeCount);
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            LibLogger.Verbose("\tReading field offsets...");
            start = DateTime.Now;
            fieldOffsets = ReadClassArrayAtVirtualAddress<long>(metadataRegistration.fieldOffsetListAddress, metadataRegistration.fieldOffsetsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading types...");
            start = DateTime.Now;
            var typesAddress = ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.typeAddressListAddress, metadataRegistration.numTypes);
            types = new Il2CppType[metadataRegistration.numTypes];
            for (var i = 0; i < metadataRegistration.numTypes; ++i)
            {
                types[i] = ReadClassAtVirtualAddress<Il2CppType>(typesAddress[i]);
                types[i].Init();
                typesDict[typesAddress[i]] = types[i];
            }

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            
            LibLogger.Verbose("\tReading type definition sizes...");
            start = DateTime.Now;
            TypeDefinitionSizePointers = ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.typeDefinitionsSizes, metadataRegistration.typeDefinitionsSizesCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            if (metadataRegistration.metadataUsages != 0)
            {
                //Empty in v27
                LibLogger.Verbose("\tReading metadata usages...");
                start = DateTime.Now;
                metadataUsages = ReadClassArrayAtVirtualAddress<ulong>(metadataRegistration.metadataUsages, (long)Math.Max((decimal) metadataRegistration.metadataUsagesCount, maxMetadataUsages));
                LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }

            if (LibCpp2IlMain.MetadataVersion >= 24.2f)
            {
                LibLogger.VerboseNewline("\tReading code gen modules...");
                start = DateTime.Now;

                var codeGenModulePtrs = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.addrCodeGenModulePtrs, (long) codeRegistration.codeGenModulesCount);
                codeGenModules = new Il2CppCodeGenModule[codeGenModulePtrs.Length];
                codeGenModuleMethodPointers = new ulong[codeGenModulePtrs.Length][];
                codegenModuleRgctxRanges = new Il2CppTokenRangePair[codeGenModulePtrs.Length][];
                codegenModuleRgctxs = new Il2CppRGCTXDefinition[codeGenModulePtrs.Length][];
                for (var i = 0; i < codeGenModulePtrs.Length; i++)
                {
                    var codeGenModule = ReadClassAtVirtualAddress<Il2CppCodeGenModule>(codeGenModulePtrs[i]);
                    codeGenModules[i] = codeGenModule;
                    string name = ReadStringToNull(MapVirtualAddressToRaw(codeGenModule.moduleName));
                    LibLogger.VerboseNewline($"\t\t-Read module data for {name}, contains {codeGenModule.methodPointerCount} method pointers starting at 0x{codeGenModule.methodPointers:X}");
                    if (codeGenModule.methodPointerCount > 0)
                    {
                        try
                        {
                            var ptrs = ReadClassArrayAtVirtualAddress<ulong>(codeGenModule.methodPointers, codeGenModule.methodPointerCount);
                            codeGenModuleMethodPointers[i] = ptrs;
                            LibLogger.VerboseNewline($"\t\t\t-Read {codeGenModule.methodPointerCount} method pointers.");
                        }
                        catch (Exception e)
                        {
                            LibLogger.VerboseNewline($"\t\t\tWARNING: Unable to get function pointers for {name}: {e.Message}");
                            codeGenModuleMethodPointers[i] = new ulong[codeGenModule.methodPointerCount];
                        }
                    }

                    if (codeGenModule.rgctxRangesCount > 0)
                    {
                        try
                        {
                            var ranges = ReadClassArrayAtVirtualAddress<Il2CppTokenRangePair>(codeGenModule.pRgctxRanges, codeGenModule.rgctxRangesCount);
                            codegenModuleRgctxRanges[i] = ranges;
                            LibLogger.VerboseNewline($"\t\t\t-Read {codeGenModule.rgctxRangesCount} RGCTX ranges.");
                        }
                        catch (Exception e)
                        {
                            LibLogger.VerboseNewline($"\t\t\tWARNING: Unable to get RGCTX ranges for {name}: {e.Message}");
                            codegenModuleRgctxRanges[i] = new Il2CppTokenRangePair[codeGenModule.rgctxRangesCount];
                        }
                    }

                    if (codeGenModule.rgctxsCount > 0)
                    {
                        try
                        {
                            var rgctxs = ReadClassArrayAtVirtualAddress<Il2CppRGCTXDefinition>(codeGenModule.rgctxs, codeGenModule.rgctxsCount);
                            codegenModuleRgctxs[i] = rgctxs;
                            LibLogger.VerboseNewline($"\t\t\t-Read {codeGenModule.rgctxsCount} RGCTXs.");
                        }
                        catch (Exception e)
                        {
                            LibLogger.VerboseNewline($"\t\t\tWARNING: Unable to get RGCTXs for {name}: {e.Message}");
                            codegenModuleRgctxs[i] = new Il2CppRGCTXDefinition[codeGenModule.rgctxsCount];
                        }
                    }
                }

                LibLogger.VerboseNewline($"\tOK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }
            else
            {
                LibLogger.Verbose("\tReading method pointers...");
                start = DateTime.Now;
                methodPointers = ReadClassArrayAtVirtualAddress<ulong>(codeRegistration.methodPointers, (long) codeRegistration.methodPointersCount);
                LibLogger.VerboseNewline($"Read {methodPointers.Length} OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
            }


            LibLogger.Verbose("\tReading generic method tables...");
            start = DateTime.Now;
            genericMethodTables = ReadClassArrayAtVirtualAddress<Il2CppGenericMethodFunctionsDefinitions>(metadataRegistration.genericMethodTable, metadataRegistration.genericMethodTableCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading method specifications...");
            start = DateTime.Now;
            methodSpecs = ReadClassArrayAtVirtualAddress<Il2CppMethodSpec>(metadataRegistration.methodSpecs, metadataRegistration.methodSpecsCount);
            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

            LibLogger.Verbose("\tReading generic methods...");
            start = DateTime.Now;
            genericMethodDictionary = new Dictionary<int, ulong>();
            foreach (var table in genericMethodTables)
            {
                var genericMethodIndex = table.genericMethodIndex;
                var genericMethodPointerIndex = table.indices.methodIndex;

                var methodDefIndex = GetGenericMethodFromIndex(genericMethodIndex, genericMethodPointerIndex, out _);

                if (!genericMethodDictionary.ContainsKey(methodDefIndex) && genericMethodPointerIndex < genericMethodPointers.Length)
                {
                    genericMethodDictionary.TryAdd(methodDefIndex, genericMethodPointers[genericMethodPointerIndex]);
                }
            }

            LibLogger.VerboseNewline($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");
        }

        private int GetGenericMethodFromIndex(int genericMethodIndex, int genericMethodPointerIndex, out Il2CppGenericMethodRef? genericMethodRef)
        {
            var methodSpec = GetMethodSpec(genericMethodIndex);
            var methodDefIndex = methodSpec.methodDefinitionIndex;
            genericMethodRef = new Il2CppGenericMethodRef(methodSpec);
            
            if (genericMethodPointerIndex >= 0)
            {
                if(genericMethodPointerIndex < genericMethodPointers.Length)
                    genericMethodRef.GenericVariantPtr = genericMethodPointers[genericMethodPointerIndex];
            }
            
            if (!ConcreteGenericMethods.ContainsKey(genericMethodRef.BaseMethod))
                ConcreteGenericMethods[genericMethodRef.BaseMethod] = new List<Il2CppGenericMethodRef>();
            
            ConcreteGenericMethods[genericMethodRef.BaseMethod].Add(genericMethodRef);

            if (genericMethodRef.GenericVariantPtr > 0)
            {
                if (!ConcreteGenericImplementationsByAddress.ContainsKey(genericMethodRef.GenericVariantPtr))
                    ConcreteGenericImplementationsByAddress[genericMethodRef.GenericVariantPtr] = new List<Il2CppGenericMethodRef>();
                
                ConcreteGenericImplementationsByAddress[genericMethodRef.GenericVariantPtr].Add(genericMethodRef);
            }

            return methodDefIndex;
        }

        public abstract byte GetByteAtRawAddress(ulong addr);
        public abstract long MapVirtualAddressToRaw(ulong uiAddr);
        public abstract ulong MapRawAddressToVirtual(uint offset);
        public abstract ulong GetRVA(ulong pointer);

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
            try
            {
                result = MapVirtualAddressToRaw(virtAddr);
                return true;
            }
            catch (Exception)
            {
                result = 0;
                return false;
            }
        }

        public T[] ReadClassArrayAtVirtualAddress<T>(ulong addr, long count) where T : new()
        {
            return ReadClassArrayAtRawAddr<T>(MapVirtualAddressToRaw(addr), count);
        }

        public T ReadClassAtVirtualAddress<T>(ulong addr) where T: new()
        {
            return ReadClassAtRawAddr<T>(MapVirtualAddressToRaw(addr));
        }

        public Il2CppGenericInst GetGenericInst(int index) => genericInsts[index];

        public Il2CppMethodSpec GetMethodSpec(int index) => index >= methodSpecs.Length
            ? throw new ArgumentException($"GetMethodSpec: index {index} >= length {methodSpecs.Length}")
            : index < 0
                ? throw new ArgumentException($"GetMethodSpec: index {index} < 0")
                : methodSpecs[index];
        public Il2CppType GetType(int index) => types[index];
        public ulong GetRawMetadataUsage(uint index) => metadataUsages[index];
        public ulong[] GetCodegenModuleMethodPointers(int codegenModuleIndex) => codeGenModuleMethodPointers[codegenModuleIndex];
        public Il2CppCodeGenModule? GetCodegenModuleByName(string name) => codeGenModules.FirstOrDefault(m => m.Name == name);
        public int GetCodegenModuleIndex(Il2CppCodeGenModule module) => Array.IndexOf(codeGenModules, module);
        public int GetCodegenModuleIndexByName(string name) => GetCodegenModuleByName(name) is { } module ? GetCodegenModuleIndex(module) : -1;
        public Il2CppTokenRangePair[] GetRGCTXRangePairsForModule(Il2CppCodeGenModule module) => codegenModuleRgctxRanges[GetCodegenModuleIndex(module)];
        public Il2CppRGCTXDefinition[] GetRGCTXDataForPair(Il2CppCodeGenModule module, Il2CppTokenRangePair rangePair) => codegenModuleRgctxs[GetCodegenModuleIndex(module)].Skip(rangePair.start).Take(rangePair.length).ToArray();

        public Il2CppType GetIl2CppTypeFromPointer(ulong pointer)
        {
            return typesDict[pointer];
        }

        public ulong[] GetPointers(ulong pointer, long count)
        {
            if (is32Bit)
                return Array.ConvertAll(ReadClassArrayAtVirtualAddress<uint>(pointer, count), x => (ulong) x);
            return ReadClassArrayAtVirtualAddress<ulong>(pointer, count);
        }

        public int GetFieldOffsetFromIndex(int typeIndex, int fieldIndexInType, int fieldIndex, bool isValueType, bool isStatic)
        {
            try
            {
                var offset = -1;
                if (LibCpp2IlMain.MetadataVersion > 21)
                {
                    var ptr = (ulong) fieldOffsets[typeIndex];
                    if (ptr > 0)
                    {
                        offset = ReadClassAtRawAddr<int>((long) ((ulong) MapVirtualAddressToRaw(ptr) + 4ul * (ulong) fieldIndexInType));
                    }
                }
                else
                {
                    offset = (int) fieldOffsets[fieldIndex];
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
                if (genericMethodDictionary.TryGetValue(methodDefinitionIndex, out var methodPointer))
                {
                    return methodPointer;
                }

                var ptrs = codeGenModuleMethodPointers[imageIndex];
                var methodPointerIndex = methodToken & 0x00FFFFFFu;
                return ptrs[methodPointerIndex - 1];
            }
            else
            {
                if (methodIndex >= 0)
                {
                    return methodPointers[methodIndex];
                }

                genericMethodDictionary.TryGetValue(methodDefinitionIndex, out var methodPointer);
                return methodPointer;
            }
        }

        public ulong GetCustomAttributeGenerator(int index) => customAttributeGenerators![index];

        public ulong[] AllCustomAttributeGenerators => LibCpp2IlMain.MetadataVersion >= 27 ? AllCustomAttributeGeneratorsV27 : customAttributeGenerators!;

        private ulong[] AllCustomAttributeGeneratorsV27 =>
            LibCpp2IlMain.TheMetadata!.imageDefinitions
                .Select(i => (image: i, cgm: GetCodegenModuleByName(i.Name!)!, ptrSize: is32Bit ? 4UL : 8UL))
                .SelectMany(tuple => LibCpp2ILUtils.Range(0, (int) tuple.image.customAttributeCount).Select(o => tuple.cgm.customAttributeCacheGenerator + (ulong) o * tuple.ptrSize))
                .Select(p => ReadClassAtVirtualAddress<ulong>(p))
                .ToArray();

        public abstract byte[] GetRawBinaryContent();
        public abstract ulong GetVirtualAddressOfExportedFunctionByName(string toFind);

        public abstract byte[] GetEntirePrimaryExecutableSection();

        public abstract ulong GetVirtualAddressOfPrimaryExecutableSection();

        public (ulong pCodeRegistration, ulong pMetadataRegistration) PlusSearch(int methodCount, int typeDefinitionsCount)
        {
            ulong pCodeRegistration = 0;
            ulong pMetadataRegistration;

            LibLogger.VerboseNewline("\tAttempting to locate code and metadata registration functions...");

            var plusSearch = new BinarySearcher(this, methodCount, typeDefinitionsCount);

            LibLogger.VerboseNewline("\t\t-Searching for MetadataReg...");
            
            pMetadataRegistration = LibCpp2IlMain.MetadataVersion < 24.5f 
                ? plusSearch.FindMetadataRegistrationPre24_5() 
                : plusSearch.FindMetadataRegistrationPost24_5();

            LibLogger.VerboseNewline("\t\t-Searching for CodeReg...");

            if (pCodeRegistration == 0)
            {
                if (LibCpp2IlMain.MetadataVersion >= 24.2f)
                {
                    LibLogger.VerboseNewline("\t\t\tUsing mscorlib full-disassembly approach to get codereg, this may take a while...");
                    pCodeRegistration = plusSearch.FindCodeRegistrationPost2019();
                }
                else
                    pCodeRegistration = plusSearch.FindCodeRegistrationPre2019();
            }

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
}