using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata
{
    public class Il2CppMethodDefinition
    {
        public int nameIndex;
        public int declaringTypeIdx;
        public int returnTypeIdx;
        public int parameterStart;
        [Version(Max = 24)] public int customAttributeIndex;
        public int genericContainerIndex;
        [Version(Max = 24.1f)] public int methodIndex;
        [Version(Max = 24.1f)] public int invokerIndex;
        [Version(Max = 24.1f)] public int delegateWrapperIndex;
        [Version(Max = 24.1f)] public int rgctxStartIndex;
        [Version(Max = 24.1f)] public int rgctxCount;
        public uint token;
        public ushort flags;
        public ushort iflags;
        public ushort slot;
        public ushort parameterCount;

        public MethodAttributes Attributes => (MethodAttributes) flags;

        public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

        public int MethodIndex => LibCpp2IlReflection.GetMethodIndexFromMethod(this);

        public string? Name => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.GetStringFromIndex(nameIndex);

        public string? GlobalKey => DeclaringType == null ? null : DeclaringType.Name + "." + Name + "()";

        public Il2CppType? RawReturnType => LibCpp2IlMain.Binary?.GetType(returnTypeIdx);
        
        public Il2CppTypeReflectionData? ReturnType => LibCpp2IlMain.Binary == null ? null : LibCpp2ILUtils.GetTypeReflectionData(LibCpp2IlMain.Binary.GetType(returnTypeIdx));

        public Il2CppTypeDefinition? DeclaringType => LibCpp2IlMain.TheMetadata == null ? null : LibCpp2IlMain.TheMetadata.typeDefs[declaringTypeIdx];

        private ulong? _methodPointer = null;
        public ulong MethodPointer
        {
            get
            {
                if (!_methodPointer.HasValue)
                {
                    if (LibCpp2IlMain.Binary == null || LibCpp2IlMain.TheMetadata == null || DeclaringType == null)
                    {
                        LibLogger.WarnNewline($"Couldn't get method pointer for {Name}. Binary is {LibCpp2IlMain.Binary}, Meta is {LibCpp2IlMain.TheMetadata}, DeclaringType is {DeclaringType}");
                        return 0;
                    }

                    var asmIdx = 0; //Not needed pre-24.2
                    if (LibCpp2IlMain.MetadataVersion >= 27)
                    {
                        asmIdx = LibCpp2IlMain.Binary.GetCodegenModuleIndexByName(DeclaringType!.DeclaringAssembly!.Name!);
                    }
                    else if (LibCpp2IlMain.MetadataVersion >= 24.2f)
                    {
                        asmIdx = DeclaringType!.DeclaringAssembly!.assemblyIndex;
                    }
                    
                    _methodPointer = LibCpp2IlMain.Binary.GetMethodPointer(methodIndex, MethodIndex, asmIdx, token);
                }

                return _methodPointer.Value;
            }
        }

        public long MethodOffsetInFile => MethodPointer == 0 || LibCpp2IlMain.Binary == null ? 0 : LibCpp2IlMain.Binary.MapVirtualAddressToRaw(MethodPointer);

        public ulong Rva => MethodPointer == 0 || LibCpp2IlMain.Binary == null ? 0 : LibCpp2IlMain.Binary.GetRVA(MethodPointer);

        public string? HumanReadableSignature => ReturnType == null || Parameters == null || Name == null ? null : $"{ReturnType} {Name}({string.Join(", ", Parameters.AsEnumerable())})";

        public Il2CppParameterDefinition[]? InternalParameterData
        {
            get
            {
                if (LibCpp2IlMain.TheMetadata == null || LibCpp2IlMain.Binary == null)
                    return null;

                if (parameterStart < 0 || parameterCount == 0)
                    return Array.Empty<Il2CppParameterDefinition>();

                var ret = new Il2CppParameterDefinition[parameterCount];

                Array.Copy(LibCpp2IlMain.TheMetadata.parameterDefs, parameterStart, ret, 0, parameterCount);

                return ret;
            }
        }

        public Il2CppType[]? InternalParameterTypes => InternalParameterData == null
            ? null
            : InternalParameterData.Select(paramDef => LibCpp2IlMain.Binary!.GetType(paramDef.typeIndex))
                .ToArray();

        private Il2CppParameterReflectionData[]? _cachedParameters;

        public Il2CppParameterReflectionData[]? Parameters
        {
            get
            {
                if (_cachedParameters == null && InternalParameterData != null)
                {
                    _cachedParameters = InternalParameterData
                        .Select((paramDef, idx) =>
                        {
                            var paramType = LibCpp2IlMain.Binary!.GetType(paramDef.typeIndex);
                            var paramFlags = (ParameterAttributes) paramType.attrs;
                            var paramDefaultData = (paramFlags & ParameterAttributes.HasDefault) != 0 ? LibCpp2IlMain.TheMetadata!.GetParameterDefaultValueFromIndex(parameterStart + idx) : null;
                            return new Il2CppParameterReflectionData
                            {
                                Type = LibCpp2ILUtils.GetTypeReflectionData(paramType)!,
                                ParameterName = LibCpp2IlMain.TheMetadata!.GetStringFromIndex(paramDef.nameIndex),
                                ParameterAttributes = paramFlags,
                                RawType = paramType,
                                DefaultValue = paramDefaultData == null ? null : LibCpp2ILUtils.GetDefaultValue(paramDefaultData.dataIndex, paramDefaultData.typeIndex),
                            };
                        }).ToArray();
                }

                return _cachedParameters;
            }
        }

        public Il2CppGenericContainer? GenericContainer => genericContainerIndex < 0 ? null : LibCpp2IlMain.TheMetadata?.genericContainers[genericContainerIndex];

        public override string ToString()
        {
            if (LibCpp2IlMain.TheMetadata == null) return base.ToString();

            return $"Il2CppMethodDefinition[Name='{Name}']";
        }
    }
}