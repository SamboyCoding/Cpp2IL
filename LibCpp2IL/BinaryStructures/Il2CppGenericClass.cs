using System;
using LibCpp2IL.Metadata;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericClass : ReadableClass
{
    [Version(Max = 24.5f)] public long TypeDefinitionIndex; /* the generic type definition */

    [Version(Min = 27.0f)] public ulong V27TypePointer;
    
    public Il2CppGenericContext Context = null!; /* a context that contains the type instantiation doesn't contain any method instantiation */
    public ulong CachedClass; /* if present, the Il2CppClass corresponding to the instantiation.  */

    private float EffectiveMetadataVersion => OwningMetadata?.MetadataVersion ?? MetadataVersion;

    public Il2CppTypeDefinition TypeDefinition
    {
        get
        {
            if (EffectiveMetadataVersion < 27f)
            {
                return OwningMetadata!.GetTypeDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage((int)TypeDefinitionIndex));
            }

            return V27BaseType!.AsClass();
        }
    }

    public Il2CppType? V27BaseType
    {
        get
        {
            if (EffectiveMetadataVersion < 27f)
                return null;

            var binary = OwningBinary;
            if (binary == null)
                return null;

            var t = binary.ReadReadableAtVirtualAddress<Il2CppType>(V27TypePointer);
            t.OwningBinary = binary;
            t.OwningMetadata = OwningMetadata;
            t.Il2CppTypeHasNumMods5Bits ??= (OwningMetadata?.MetadataVersion ?? MetadataVersion) >= 27.2f;
            return t;
        }
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtLeast(27f))
            V27TypePointer = reader.ReadNUint();
        else
            TypeDefinitionIndex = reader.ReadNInt();
        
        Context = reader.ReadReadableHereNoLock<Il2CppGenericContext>();
        Context.OwningBinary = OwningBinary;
        CachedClass = reader.ReadNUint();
    }
}
