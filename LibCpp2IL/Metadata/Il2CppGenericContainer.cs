using System.Collections.Generic;
using System.Diagnostics;

namespace LibCpp2IL.Metadata;

public class Il2CppGenericContainer : ReadableClass
{
    /* index of the generic type definition or the generic method definition corresponding to this container */
    public int ownerIndex; // either index into Il2CppClass metadata array or Il2CppMethodDefinition array

    //Number of generic arguments
    public int genericParameterCount; //int32 pre-v106, uint16 post-v106

    /* If true, we're a generic method, otherwise a generic type definition. */
    public bool isGenericMethod; // actually an int32 pre-v106 (and a byte post-v106), but we treat it as a bool

    /* Our type parameters. */
    public Il2CppVariableWidthIndex<Il2CppGenericParameter> genericParameterStart;

    public IEnumerable<Il2CppGenericParameter> GenericParameters
    {
        get
        {
            if (genericParameterCount == 0)
                yield break;

            for (var i = 0; i < genericParameterCount; i++)
            {
                var index = Il2CppVariableWidthIndex<Il2CppGenericParameter>.MakeTemporaryForFixedWidthUsage(genericParameterStart.Value + i); //DynWidth: computed, not read, so temp is fine
                var p = OwningContext.Metadata.GetGenericParameterFromIndex(index);
                p.Index = index;
                Debug.Assert(p.genericParameterIndexInOwner == i);
                yield return p;
            }
        }
    }

    //DynWidth: ownerIndex is always int, so making temp is ok
    public Il2CppTypeDefinition? TypeOwner => isGenericMethod ? null : OwningContext.Metadata.GetTypeDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage(ownerIndex));

    //DynWidth: ownerIndex is always int, so making temp is ok
    public Il2CppMethodDefinition? MethodOwner => isGenericMethod ? OwningContext.Metadata.GetMethodDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppMethodDefinition>.MakeTemporaryForFixedWidthUsage(ownerIndex)) : null;

    public Il2CppTypeDefinition? DeclaringType => TypeOwner ?? MethodOwner?.DeclaringType;

    public override void Read(ClassReadingBinaryReader reader)
    {
        ownerIndex = reader.ReadInt32();

        if (IsAtLeast(106f))
        {
            genericParameterCount = reader.ReadUInt16();
            isGenericMethod = reader.ReadByte() != 0;
        }
        else
        {
            genericParameterCount = reader.ReadInt32();
            isGenericMethod = reader.ReadInt32() != 0;
        }

        genericParameterStart = Il2CppVariableWidthIndex<Il2CppGenericParameter>.Read(reader);
    }
}
