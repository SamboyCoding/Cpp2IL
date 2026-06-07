using System;
using System.Threading;

namespace LibCpp2IL.Metadata;

/// <summary>
/// Represents an index into an array used by il2cpp. Usually this array is in the metadata but not always.
/// The width of the index is determined at build time by the number of items in the array - it is always 32, 16, or 8 bits.
/// On metadata versions prior to v38, the width is always 32-bit (these versions simply used int32_t).
/// On metadata versions >= v38, the width MAY be dynamic based on version and which arrays has been converted to use this system in that version:
/// - v38 uses dynamic widths for TypeDefinition, GenericContainer, and Type (in the binary)
/// - v39 extends this to ParameterDefinition
/// - v104 extends this to InterfaceIndex, EventIndex, PropertyIndex, and NestedTypeIndex
/// - v105 extends this to MethodIndex 
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly record struct Il2CppVariableWidthIndex<T> where T : ReadableClass
{
    //Statics here are per-T.
    private static int widthForThisTypeOnCurrentApplication = -1; // -1 means "not yet determined"
    
    /// <summary>
    /// Used to ensure that multiple threads reading in parallel don't clobber each other's width values.
    /// Cpp2IL doesn't yet support multiple applications being held in the same process at once, but I do want to eventually strip out all static state so it can function that way.
    /// This lock is held while e.g. one application (metadata + binary) is being read, then released, so that another application can be read in the same process without interference.
    /// That way if you try to init another application while one is already initializing, that second one will wait until the first one is done, then determine its own widths and not interfere with the first one.
    /// </summary>
    private static object readSessionLock = new object();
    
    private readonly int value;

    public bool IsNull => value < 0; //The exact value of the "null" index depends on the width
    public bool IsNonNull => !IsNull;
    public int Value => value;
    
    public static Il2CppVariableWidthIndex<T> operator +(Il2CppVariableWidthIndex<T> index, Il2CppVariableWidthIndex<T> offset)
        => new(index.value + offset.value);

    public Il2CppVariableWidthIndex() 
        => throw new NotSupportedException("Do not manually construct an Il2CppVariableWidthIndex. This struct is only intended to be used as a field type in other ReadableClass types, and should be read using the static Read method.");

    private Il2CppVariableWidthIndex(int value) 
        => this.value = value;
    
    public static Il2CppVariableWidthIndex<T> Null => new(-1);
    
    /// <summary>
    /// Used for fields which are of int-type because they were removed before dynamic-width was introduced, when passing to an API which requires an Il2CppVariableWidthIndex.
    /// For example, <see cref="Il2CppMetadata.attributeTypes"/> was removed in v29, long before dynamic widths were introduced, so it is defined as int[] for performance, but when passing its values to APIs which require an Il2CppVariableWidthIndex&lt;Il2CppType&gt; this method can be used to create a temporary Il2CppVariableWidthIndex with the correct value.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static Il2CppVariableWidthIndex<T> MakeTemporaryForFixedWidthUsage(int value)
        => new(value);

    public static Il2CppVariableWidthIndex<T> Read(ClassReadingBinaryReader reader)
    {
        if (widthForThisTypeOnCurrentApplication == -1)
            throw new Exception($"Attempted to read an Il2CppVariableWidthIndex before determining the width for this type ({typeof(T)}) on this application. This should never happen, but if it does, it means something has gone very wrong with the threading in Cpp2IL. Please report this to the developers.");

        switch (widthForThisTypeOnCurrentApplication)
        {
            case 1:
                var val = reader.ReadByte();
                if(val == byte.MaxValue)
                    return Null;
                return new(val);
            case 2:
                var val2 = reader.ReadUInt16();
                if(val2 == ushort.MaxValue)
                    return Null;
                return new(val2);
            case 4:
                return new(reader.ReadInt32());
            default:
                throw new Exception($"Invalid width {widthForThisTypeOnCurrentApplication} for Il2CppVariableWidthIndex of type {typeof(T)}.");
        }
    }

    internal static void BeginReadSession(int width)
    {
        var lockTaken = false;
        Monitor.Enter(readSessionLock, ref lockTaken);
        if(!lockTaken)
            throw new Exception("Failed to acquire read session lock for Il2CppVariableWidthIndex. This should never happen, but if it does, it means something has gone very wrong with the threading in Cpp2IL. Please report this to the developers.");
        
        widthForThisTypeOnCurrentApplication = width;
    }

    internal static void BeginReadSessionOnLegacyVersion()
    {
        var lockTaken = false;
        Monitor.Enter(readSessionLock, ref lockTaken);
        if(!lockTaken)
            throw new Exception("Failed to acquire read session lock for Il2CppVariableWidthIndex. This should never happen, but if it does, it means something has gone very wrong with the threading in Cpp2IL. Please report this to the developers.");
        
        widthForThisTypeOnCurrentApplication = 4; //Legacy versions always use 4-byte widths
    }
    
    internal static void EndReadSession()
    {
        widthForThisTypeOnCurrentApplication = -1;
        Monitor.Exit(readSessionLock);
    }
}
