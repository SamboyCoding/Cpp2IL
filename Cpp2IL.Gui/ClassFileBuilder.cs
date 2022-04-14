using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Gui;

public static class ClassFileBuilder
{
    public static string BuildCsFileForType(TypeAnalysisContext type, MethodBodyMode methodBodyMode, bool includeAttributeGenerators)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(type.Definition!.Namespace))
        {
            sb.Append("namespace ").AppendLine(type.Definition.Namespace).AppendLine("{");
            sb.AppendLine();
        }

        //Type custom attributes
        sb.Append(GetCustomAttributeStrings(type, 0));

        //Type keywords and name
        sb.Append(GetKeyWordsForType(type)).Append(type.Definition.Name);

        //Base class
        var baseType = type.Definition.BaseType;
        var needsBaseClass = baseType != null && baseType.ToString() is not "System.Object" and not "System.ValueType" and not "System.Enum";
        if (needsBaseClass)
            sb.Append(" : ").Append(GetTypeName(baseType!.ToString()));

        //Interfaces
        if (type.Definition.Interfaces!.Length > 0)
        {
            if (!needsBaseClass)
                sb.Append(" : ");

            var addComma = needsBaseClass;
            foreach (var @interface in type.Definition.Interfaces)
            {
                if (addComma)
                    sb.Append(", ");

                addComma = true;

                sb.Append(GetTypeName(@interface.ToString()));
            }
        }

        //Opening brace on new line
        sb.AppendLine();
        sb.AppendLine("{");

        if (IsEnum(type))
        {
            //enums - all fields that are constants are enum values
            //no methods, no props, no events
            foreach (var field in type.Fields)
            {
                if (field.BackingData!.Attributes.HasFlag(FieldAttributes.Literal))
                {
                    sb.Append('\t').Append(field.BackingData.Field.Name).Append(" = ").Append(field.BackingData.DefaultValue).AppendLine(",");
                }
            }
        }
        else
        {
            //Fields
            foreach (var field in type.Fields)
            {
                sb.Append(GetCustomAttributeStrings(field, 1));

                sb.Append('\t').Append(GetKeyWordsForField(field));
                sb.Append(GetTypeName(field.BackingData!.Field.FieldType!.ToString())).Append(' ');
                sb.Append(field.BackingData.Field.Name!);

                var isConst = field.BackingData.Attributes.HasFlag(FieldAttributes.Literal);
                if (isConst)
                    sb.Append(" = ").Append(field.BackingData.DefaultValue);

                sb.Append(';');

                if (!isConst)
                {
                    var offset = type.AppContext.Binary.GetFieldOffsetFromIndex(type.Definition.TypeIndex, type.Fields.IndexOf(field), field.BackingData.Field.FieldIndex, type.Definition.IsValueType, field.BackingData.Attributes.HasFlag(FieldAttributes.Static));
                    sb.Append(" // C++ Field Offset: ").Append(offset).Append(" (0x").Append(offset.ToString("X")).Append(')');
                }

                sb.AppendLine();
                sb.AppendLine();
            }

            if (type.Methods.Count > 0)
                sb.AppendLine();

            //Constructors
            foreach (var method in type.Methods)
            {
                if (method.Definition!.Name is not ".ctor" and not ".cctor")
                    continue;

                if (method.Definition.MethodPointer > 0)
                    sb.Append('\t').Append("// Method at address 0x").Append(method.Definition.MethodPointer.ToString("X")).AppendLine();

                sb.Append(GetCustomAttributeStrings(method, 1));
                sb.Append('\t').Append(GetKeyWordsForMethod(method)).Append(type.Definition.Name).Append('(');
                sb.Append(GetMethodParameterString(method));
                sb.Append(')');

                sb.Append(GetMethodBodyIfPresent(method, methodBodyMode));
            }

            //Properties
            foreach (var prop in type.Properties)
            {
                prop.AnalyzeCustomAttributeData();

                //TODO
                // sb.Append(GetCustomAttributeStrings(prop, 1));
                //
                // sb.Append('\t').Append(GetKeyWordsForProperty(prop));
                // sb.Append(GetTypeName(prop.Definition.PropertyType!.ToString())).Append(' ');
                // sb.Append(prop.Definition.Name!);
                //
                // sb.Append(' ').Append(GetMethodParameterString(prop));
                //
                // sb.Append(GetMethodBodyIfPresent(prop, methodBodyMode));
            }

            //Methods
            foreach (var method in type.Methods)
            {
                if (method.Definition!.Name is ".ctor" or ".cctor")
                    continue;

                if (method.Definition.MethodPointer > 0)
                    sb.Append('\t').Append("// Method at address 0x").Append(method.Definition.MethodPointer.ToString("X")).AppendLine();

                sb.Append(GetCustomAttributeStrings(method, 1));
                sb.Append('\t').Append(GetKeyWordsForMethod(method));
                sb.Append(GetTypeName(method.Definition.ReturnType!.ToString())).Append(' ');
                sb.Append(method.Definition!.Name).Append('(');
                sb.Append(GetMethodParameterString(method));
                sb.Append(')');

                sb.Append(GetMethodBodyIfPresent(method, methodBodyMode));
            }

            //Attribute generators, if enabled and available
            if (includeAttributeGenerators && type.AppContext.MetadataVersion < 29f)
            {
                var membersWithGenerators = type.Methods.Cast<HasCustomAttributes>().Concat(type.Fields).Concat(type.Properties).Append(type).ToList();

                foreach (var memberWithGenerator in membersWithGenerators)
                {
                    if (memberWithGenerator.RawIl2CppCustomAttributeData.Length == 0)
                        continue;

                    sb.Append("\t// Custom attribute generator at address 0x").Append(memberWithGenerator.CaCacheGeneratorAnalysis!.UnderlyingPointer.ToString("X")).AppendLine();
                    sb.AppendLine("\t// Expected custom attribute types (parameter+ptr contains an array of these): ");

                    foreach (var il2CppType in memberWithGenerator.AttributeTypes!)
                    {
                        sb.Append("\t//\t");
                        
                        if (il2CppType.Type is Il2CppTypeEnum.IL2CPP_TYPE_CLASS or Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE)
                            sb.Append(GetTypeName(il2CppType.AsClass().FullName!));
                        else
                            sb.Append(GetTypeName(LibCpp2ILUtils.GetTypeReflectionData(il2CppType).ToString()));
                        
                        sb.AppendLine();
                    }

                    sb.Append("\tpublic static void GenerateCustomAttributesFor_");
                    sb.Append(memberWithGenerator switch
                    {
                        MethodAnalysisContext => "Method",
                        FieldAnalysisContext => "Field",
                        PropertyAnalysisContext => "Property",
                        TypeAnalysisContext => "Type",
                        _ => throw new ArgumentOutOfRangeException()
                    });
                    sb.Append('_').Append(memberWithGenerator.CustomAttributeOwnerName).Append("(Il2CppCustomAttributeCache customAttributes)");

                    sb.Append(GetMethodBodyIfPresent(memberWithGenerator.CaCacheGeneratorAnalysis, methodBodyMode));
                }
            }
        }

        sb.AppendLine("}"); //Close class

        if (!string.IsNullOrWhiteSpace(type.Definition.Namespace))
            sb.Append('}'); //Close namespace

        return sb.ToString();
    }

    private static string GetMethodBodyIfPresent(MethodAnalysisContext method, MethodBodyMode mode)
    {
        var sb = new StringBuilder();

        if (method.Definition?.Attributes.HasFlag(MethodAttributes.Abstract) ?? false)
            return ";\n\n";

        sb.AppendLine();
        sb.AppendLine("\t{");

        if (mode != MethodBodyMode.Stubs)
        {
            try
            {
                if (mode != MethodBodyMode.RawAsm)
                    method.Analyze();

                switch (mode)
                {
                    case MethodBodyMode.Isil:
                    {
                        sb.AppendLine("\t\t// Method body (Instruction-Set-Independent Machine Code Representation)");

                        if (method.ConvertedIsil == null)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"\t\t// ERROR: No ISIL was generated, which probably means the {method.AppContext.InstructionSet.GetType().Name}\n\t\t// is not fully implemented and so does not generate control flow graphs.");
                        }
                        else
                            sb.Append(GetMethodBodyISIL(method.ConvertedIsil));

                        break;
                    }
                    case MethodBodyMode.RawAsm:
                    {
                        var rawBytes = method.AppContext.InstructionSet.GetRawBytesForMethod(method, false);

                        sb.AppendLine($"\t\t// Method body (Raw Machine Code, {rawBytes.Length} bytes)").AppendLine();

                        sb.Append("\t\t// ");
                        sb.Append(method.AppContext.InstructionSet.PrintAssembly(method).Replace("\n", "\n\t\t// ")).AppendLine();

                        break;
                    }
                    case MethodBodyMode.Pseudocode:
                    {
                        sb.AppendLine("\t\t// Method body (Generated C#-Like Decompilation)").AppendLine();

                        sb.AppendLine("\t\t// TODO: Implement C#-like decompilation");

                        break;
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("\t\t// Error Analysing method: " + e.ToString().Replace("\n", "\n\t\t//"));
            }
        }

        sb.AppendLine("\t}\n");

        return sb.ToString();
    }

    // ReSharper disable once InconsistentNaming
    private static string GetMethodBodyISIL(List<InstructionSetIndependentInstruction> method)
    {
        var sb = new StringBuilder();

        foreach (var instruction in method)
        {
            // foreach (var nodeStatement in node.Statements)
            // {
                // if (nodeStatement is IsilIfStatement ifStatement)
                // {
                //     sb.AppendLine().Append('\t', 2).Append(ifStatement.Condition).AppendLine(";\n");
                //
                //     sb.Append('\t', 2).AppendLine("// True branch");
                //     var tempBlock = new InstructionSetIndependentNode() {Statements = ifStatement.IfBlock};
                //     sb.Append(GetMethodBodyISIL(new() {tempBlock}));
                //
                //     if ((ifStatement.ElseBlock?.Count ?? 0) > 0)
                //     {
                //         sb.Append('\n').Append('\t', 2).AppendLine("// False branch");
                //         tempBlock = new() {Statements = ifStatement.ElseBlock!};
                //         sb.Append(GetMethodBodyISIL(new() {tempBlock}));
                //     }
                //
                //     sb.Append('\t', 2).AppendLine("// End of if\n");
                // }
                // else
                    sb.Append('\t', 2).Append(instruction).AppendLine(";");
            // }
        }

        return sb.ToString();
    }

    private static string GetMethodParameterString(MethodAnalysisContext method)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var paramData in method.Definition!.Parameters!)
        {
            if (!first)
                sb.Append(", ");

            first = false;

            sb.Append(GetTypeName(paramData.Type.ToString())).Append(' ').Append(paramData.ParameterName);

            if (paramData.Attributes.HasFlag(ParameterAttributes.HasDefault))
                sb.Append(" = ").Append(paramData.DefaultValue);
        }

        return sb.ToString();
    }

    private static string GetKeyWordsForType(TypeAnalysisContext type)
    {
        var sb = new StringBuilder();
        var attributes = type.Definition!.Attributes;

        if (attributes.HasFlag(TypeAttributes.Public))
            sb.Append("public ");
        else
            sb.Append("internal "); //private classes don't exist, for obvious reasons

        if (IsEnum(type))
            sb.Append("enum ");
        else if (type.Definition.BaseType?.ToString() == "System.ValueType")
            sb.Append("struct ");
        else if (attributes.HasFlag(TypeAttributes.Interface))
            sb.Append("interface ");
        else
        {
            if (attributes.HasFlag(TypeAttributes.Abstract) && attributes.HasFlag(TypeAttributes.Sealed))
                //Abstract Sealed => Static
                sb.Append("static ");
            else if (attributes.HasFlag(TypeAttributes.Abstract))
                sb.Append("abstract ");
            else if (attributes.HasFlag(TypeAttributes.Sealed))
                sb.Append("sealed ");

            sb.Append("class ");
        }

        return sb.ToString();
    }

    private static string GetKeyWordsForField(FieldAnalysisContext field)
    {
        var sb = new StringBuilder();
        var attributes = field.BackingData!.Attributes;

        if (attributes.HasFlag(FieldAttributes.Public))
            sb.Append("public ");
        else if (attributes.HasFlag(FieldAttributes.Family))
            sb.Append("protected ");
        if (attributes.HasFlag(FieldAttributes.Assembly))
            sb.Append("internal ");
        else if (attributes.HasFlag(FieldAttributes.Private))
            sb.Append("private ");

        if (attributes.HasFlag(FieldAttributes.Literal))
            sb.Append("const ");
        else
        {
            if (attributes.HasFlag(FieldAttributes.Static))
                sb.Append("static ");

            if (attributes.HasFlag(FieldAttributes.InitOnly))
                sb.Append("readonly ");
        }

        return sb.ToString();
    }

    private static string GetKeyWordsForMethod(MethodAnalysisContext method)
    {
        var sb = new StringBuilder();
        var attributes = method.Definition!.Attributes;

        if (attributes.HasFlag(MethodAttributes.Public))
            sb.Append("public ");
        else if (attributes.HasFlag(MethodAttributes.Family))
            sb.Append("protected ");
        if (attributes.HasFlag(MethodAttributes.Assembly))
            sb.Append("internal ");
        else if (attributes.HasFlag(MethodAttributes.Private))
            sb.Append("private ");
        if (attributes.HasFlag(MethodAttributes.Static))
            sb.Append("static ");

        if (method.DeclaringType!.Definition!.Attributes.HasFlag(TypeAttributes.Interface))
        {
            //Deliberate no-op to avoid unnecessarily marking interface methods as abstract
        }
        else if (attributes.HasFlag(MethodAttributes.Abstract))
            sb.Append("abstract ");
        else if (attributes.HasFlag(MethodAttributes.NewSlot))
            sb.Append("override ");
        else if (attributes.HasFlag(MethodAttributes.Virtual))
            sb.Append("virtual ");


        return sb.ToString();
    }

    private static string GetCustomAttributeStrings(HasCustomAttributes context, int indentCount)
    {
        var sb = new StringBuilder();

        context.AnalyzeCustomAttributeData();

        foreach (var analyzedCustomAttribute in context.CustomAttributes!)
        {
            if (indentCount > 0)
                sb.Append('\t', indentCount);

            sb.AppendLine(analyzedCustomAttribute.ToString());
        }

        return sb.ToString();
    }

    private static string GetTypeName(string originalName)
    {
        if (originalName.Contains('`'))
            //Generics - remove `1 etc
            return originalName.Remove(originalName.IndexOf('`'), 2);

        return originalName switch
        {
            "System.Void" => "void",
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.String" => "string",
            "System.Object" => "object",
            _ => originalName
        };
    }

    private static bool IsEnum(TypeAnalysisContext type)
        => ((TypeAttributes) type.Definition!.Flags).HasFlag(TypeAttributes.Sealed) && type.Fields.Any(f => f.BackingData!.Field.Name == "value__");
}