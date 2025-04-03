using System.Collections.Generic;

namespace LibCpp2IL.Metadata;

public class Il2CppGenericContainer : ReadableClass
{
    /* index of the generic type definition or the generic method definition corresponding to this container */
    public int ownerIndex; // either index into Il2CppClass metadata array or Il2CppMethodDefinition array

    //Number of generic arguments
    public int genericParameterCount;

    /* If true, we're a generic method, otherwise a generic type definition. */
    public int isGenericMethod;

    /* Our type parameters. */
    public int genericParameterStart;

    private bool IsOwnedByMethod => isGenericMethod != 0;

    public IEnumerable<Il2CppGenericParameter> GenericParameters
    {
        get
        {
            if (genericParameterCount == 0)
                yield break;

            var end = genericParameterStart + genericParameterCount;
            for (var i = genericParameterStart; i < end; i++)
            {
                var p = LibCpp2IlMain.TheMetadata!.genericParameters[i];
                p.Index = i;
                yield return p;
            }
        }
    }

    public Il2CppTypeDefinition? TypeOwner
    {
        get
        {
            return IsOwnedByMethod ? null : LibCpp2IlMain.TheMetadata!.typeDefs[ownerIndex];
        }
    }

    public Il2CppMethodDefinition? MethodOwner
    {
        get
        {
            return IsOwnedByMethod ? LibCpp2IlMain.TheMetadata!.methodDefs[ownerIndex] : null;
        }
    }

    public Il2CppTypeDefinition? DeclaringType => TypeOwner ?? MethodOwner.DeclaringType;

    public override void Read(ClassReadingBinaryReader reader)
    {
        ownerIndex = reader.ReadInt32();
        genericParameterCount = reader.ReadInt32();
        isGenericMethod = reader.ReadInt32();
        genericParameterStart = reader.ReadInt32();
    }
}
