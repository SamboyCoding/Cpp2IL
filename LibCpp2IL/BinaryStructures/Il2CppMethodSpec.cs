using System.Linq;
using System.Text;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppMethodSpec : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> methodDefinitionIndex;
    public Il2CppVariableWidthIndex<Il2CppGenericInst> classIndexIndex;
    public Il2CppVariableWidthIndex<Il2CppGenericInst> methodIndexIndex;

    public Il2CppMethodDefinition? MethodDefinition
        => OwningContext.Metadata.GetMethodDefinitionFromIndex(methodDefinitionIndex);

    public Il2CppGenericInst? GenericClassInst
    {
        get
        {
            if (classIndexIndex.IsNull) return null;
            return OwningContext.Binary.GetGenericInst(classIndexIndex);
        }
    }

    public Il2CppGenericInst? GenericMethodInst
    {
        get
        {
            if (methodIndexIndex.IsNull) return null;
            return OwningContext.Binary.GetGenericInst(methodIndexIndex);
        }
    }

    public Il2CppType[] GenericClassParams => classIndexIndex.IsNull ? [] : GenericClassInst!.Types;

    public Il2CppType[] GenericMethodParams => methodIndexIndex.IsNull ? [] : GenericMethodInst!.Types;

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append(MethodDefinition?.ReturnType).Append(" ");

        sb.Append(MethodDefinition?.DeclaringType?.FullName);

        if (classIndexIndex.IsNonNull)
            sb.Append("<").Append(string.Join(", ", GenericClassParams.AsEnumerable())).Append(">");

        sb.Append(".").Append(MethodDefinition?.Name);

        if (methodIndexIndex.IsNonNull)
            sb.Append("<").Append(string.Join(", ", GenericMethodParams.AsEnumerable())).Append(">");

        return sb.ToString();
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtLeast(108))
        {
            //in metadata now, so dynamic widths apply
            methodDefinitionIndex = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
            classIndexIndex = Il2CppVariableWidthIndex<Il2CppGenericInst>.Read(reader);
            methodIndexIndex = Il2CppVariableWidthIndex<Il2CppGenericInst>.Read(reader);
            return;
        }

        methodDefinitionIndex = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.MakeTemporaryForFixedWidthUsage(reader.ReadInt32());
        classIndexIndex = Il2CppVariableWidthIndex<Il2CppGenericInst>.MakeTemporaryForFixedWidthUsage(reader.ReadInt32());
        methodIndexIndex = Il2CppVariableWidthIndex<Il2CppGenericInst>.MakeTemporaryForFixedWidthUsage(reader.ReadInt32());
    }
};
