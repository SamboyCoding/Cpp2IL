using LibCpp2IL.Metadata;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericMethodFunctionsDefinitions : ReadableClass
{
    public int GenericMethodIndex;
    public int methodIndex;
    public int invokerIndex;

    //Present in v27.1 and v24.5, but not v27.0. In v108+ only present in the separate "with adjustor" metadata table.
    [Version(Min = 27.1f, Max = 108)] [Version(Min = 24.5f, Max = 24.5f)]
    public int adjustorThunk;

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtLeast(108))
        {
            GenericMethodIndex = Il2CppVariableWidthIndex<Il2CppMethodSpec>.Read(reader).Value;
            methodIndex = Il2CppVariableWidthIndex<Il2CppGenericMethodPointerTableDummy>.Read(reader).Value;
            invokerIndex = Il2CppVariableWidthIndex<Il2CppInvokerTableDummy>.Read(reader).Value;
            return;
        }

        GenericMethodIndex = reader.ReadInt32();
        methodIndex = reader.ReadInt32();
        invokerIndex = reader.ReadInt32();

        if (IsAtLeast(24.5f) && IsNot(27f))
            adjustorThunk = reader.ReadInt32();
    }
}

//v108+ only. Entries whose method has an adjustor thunk live in this separate metadata table.
public class Il2CppGenericMethodFunctionsDefinitionsWithAdjustor : Il2CppGenericMethodFunctionsDefinitions
{
    public override void Read(ClassReadingBinaryReader reader)
    {
        base.Read(reader);
        adjustorThunk = Il2CppVariableWidthIndex<Il2CppAdjustorThunkTableDummy>.Read(reader).Value;
    }
}
