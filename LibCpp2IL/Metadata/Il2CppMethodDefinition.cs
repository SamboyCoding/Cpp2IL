using System.Linq;
using System.Reflection;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.Metadata;

public class Il2CppMethodDefinition : ReadableClass
{
    public int nameIndex;
    public Il2CppVariableWidthIndex<Il2CppTypeDefinition> declaringTypeIdx;
    public Il2CppVariableWidthIndex<Il2CppType> returnTypeIdx;
    [Version(Min = 31)] public uint returnParameterToken;
    public Il2CppVariableWidthIndex<Il2CppParameterDefinition> parameterStart;
    [Version(Max = 24)] public int customAttributeIndex;
    public Il2CppVariableWidthIndex<Il2CppGenericContainer> genericContainerIndex;
    [Version(Max = 24.15f)] public int methodIndex;
    [Version(Max = 24.15f)] public int invokerIndex;
    [Version(Max = 24.15f)] public int delegateWrapperIndex;
    [Version(Max = 24.15f)] public int rgctxStartIndex;
    [Version(Max = 24.15f)] public int rgctxCount;
    public uint token;
    public ushort flags;
    public ushort iflags;
    public ushort slot;
    public ushort parameterCount;

    public MethodAttributes Attributes => (MethodAttributes)flags;

    public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> MethodIndex => OwningContext.ReflectionCache.GetMethodIndexFromMethod(this);

    public string? Name { get; private set; }

    public string? GlobalKey => DeclaringType == null ? null : DeclaringType.Name + "." + Name + "()";

    public Il2CppType? RawReturnType => OwningContext.Binary.GetType(returnTypeIdx);

    public Il2CppTypeReflectionData? ReturnType => LibCpp2ILUtils.GetTypeReflectionData(OwningContext.Binary.GetType(returnTypeIdx));

    public Il2CppTypeDefinition? DeclaringType => OwningContext.Metadata.GetTypeDefinitionFromIndex(declaringTypeIdx);

    private ulong? _methodPointer = null;

    public ulong MethodPointer
    {
        get
        {
            if (!_methodPointer.HasValue)
            {
                if (DeclaringType == null)
                {
                    LibLogger.WarnNewline($"Couldn't get method pointer for {Name}. DeclaringType is null");
                    return 0;
                }

                var asmIdx = 0; //Not needed pre-24.2
                if (MetadataVersion >= 27)
                {
                    asmIdx = OwningContext.Binary.GetCodegenModuleIndexByName(DeclaringType!.DeclaringAssembly!.Name!);
                }
                else if (MetadataVersion >= 24.2f)
                {
                    asmIdx = DeclaringType!.DeclaringAssembly!.assemblyIndex;
                }

                _methodPointer = OwningContext.Binary.GetMethodPointer(methodIndex, MethodIndex, asmIdx, token);
            }

            return _methodPointer.Value;
        }
    }

    public long MethodOffsetInFile => MethodPointer == 0 ? 0 : OwningContext.Binary.TryMapVirtualAddressToRaw(MethodPointer, out var ret) ? ret : 0;

    public ulong Rva => MethodPointer == 0 ? 0 : OwningContext.Binary.GetRva(MethodPointer);

    public string? HumanReadableSignature => ReturnType == null || Parameters == null || Name == null ? null : $"{ReturnType} {Name}({string.Join(", ", Parameters.AsEnumerable())})";

    public Il2CppParameterDefinition[]? InternalParameterData
    {
        get
        {
            if (parameterStart.IsNull || parameterCount == 0)
                return [];

            var ret = new Il2CppParameterDefinition[parameterCount];

            for (var i = 0; i < parameterCount; i++)
            {
                ret[i] = OwningContext.Metadata.GetParameterDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition>.MakeTemporaryForFixedWidthUsage(parameterStart.Value + i));
            }

            return ret;
        }
    }

    public Il2CppType[]? InternalParameterTypes => InternalParameterData == null
        ? null
        : InternalParameterData.Select(paramDef => OwningContext.Binary.GetType(paramDef.typeIndex))
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
                        var paramType = OwningContext.Binary.GetType(paramDef.typeIndex);
                        var paramFlags = (ParameterAttributes)paramType.Attrs;
                        var paramDefaultData = (paramFlags & ParameterAttributes.HasDefault) != 0 
                            ? OwningContext.Metadata.GetParameterDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition>.MakeTemporaryForFixedWidthUsage(parameterStart.Value + idx)) //DynamicWidth: value is computed so temp usage is ok
                            : null;
                        return new Il2CppParameterReflectionData
                        {
                            Type = LibCpp2ILUtils.GetTypeReflectionData(paramType)!,
                            ParameterName = OwningContext.Metadata.GetStringFromIndex(paramDef.nameIndex),
                            Attributes = paramFlags,
                            RawType = paramType,
                            DefaultValue = paramDefaultData == null ? null : LibCpp2ILUtils.GetDefaultValue(paramDefaultData.dataIndex, paramDefaultData.typeIndex, OwningContext),
                            ParameterIndex = idx,
                        };
                    }).ToArray();
            }

            return _cachedParameters;
        }
    }

    public Il2CppGenericContainer? GenericContainer => genericContainerIndex.IsNull ? null : OwningContext.Metadata.GetGenericContainerFromIndex(genericContainerIndex);
    
    public bool IsUnmanagedCallersOnly => (iflags & 0xF000) != 0;
    
    public MethodImplAttributes MethodImplAttributes => (MethodImplAttributes)(iflags & ~0xF000);

    public override string? ToString()
    {
        return $"Il2CppMethodDefinition[Name='{Name}', ReturnType={ReturnType}, DeclaringType={DeclaringType}]";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();

        //Cache name now
        var pos = reader.Position;
        Name = ((Il2CppMetadata)reader).ReadStringFromIndexNoReadLock(nameIndex);
        reader.Position = pos;

        declaringTypeIdx = Il2CppVariableWidthIndex<Il2CppTypeDefinition>.Read(reader);
        returnTypeIdx = Il2CppVariableWidthIndex<Il2CppType>.Read(reader);

        if (IsAtLeast(31))
            returnParameterToken = reader.ReadUInt32();

        parameterStart = Il2CppVariableWidthIndex<Il2CppParameterDefinition>.Read(reader);

        if (IsAtMost(24))
            customAttributeIndex = reader.ReadInt32();

        genericContainerIndex = Il2CppVariableWidthIndex<Il2CppGenericContainer>.Read(reader);

        if (IsAtMost(24.15f))
        {
            methodIndex = reader.ReadInt32();
            invokerIndex = reader.ReadInt32();
            delegateWrapperIndex = reader.ReadInt32();
            rgctxStartIndex = reader.ReadInt32();
            rgctxCount = reader.ReadInt32();
        }

        token = reader.ReadUInt32();

        flags = reader.ReadUInt16();
        iflags = reader.ReadUInt16();
        slot = reader.ReadUInt16();
        parameterCount = reader.ReadUInt16();
    }
}
