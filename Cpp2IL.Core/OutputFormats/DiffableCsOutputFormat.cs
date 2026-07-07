using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;

namespace Cpp2IL.Core.OutputFormats;

public class DiffableCsOutputFormat : Cpp2IlOutputFormat
{
    public static bool IncludeMethodLength = false;

    public override string OutputFormatId => "diffable-cs";
    public override string OutputFormatName => "Diffable C#";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        //General principle of diffable CS:
        //- Same-line method bodies ({ })
        //- Attributes in alphabetical order
        //- Members in alphabetical order and in nested type-field-event-prop-method member order
        //- No info on addresses or tokens as these change with every rebuild

        //The idea is to make it as easy as possible for software like WinMerge, github, etc, to diff the two versions of the code and show the user exactly what changed.

        outputRoot = Path.Combine(outputRoot, "DiffableCs");

        if (Directory.Exists(outputRoot))
        {
            Logger.InfoNewline("Removing old DiffableCs output directory...", "DiffableCsOutputFormat");
            Directory.Delete(outputRoot, true);
        }

        Logger.InfoNewline("Building C# files and directory structure...", "DiffableCsOutputFormat");
        var files = BuildOutput(context, outputRoot);

        Logger.InfoNewline("Writing C# files...", "DiffableCsOutputFormat");
        foreach (var (filePath, fileContent) in files)
        {
            File.WriteAllText(filePath, fileContent.ToString());
        }
    }

    private static Dictionary<string, StringBuilder> BuildOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        var ret = new Dictionary<string, StringBuilder>();

        foreach (var assembly in context.Assemblies)
        {
            var asmPath = Path.Combine(outputRoot, assembly.CleanAssemblyName);
            Directory.CreateDirectory(asmPath);

            foreach (var type in assembly.TopLevelTypes)
            {
                if (type is InjectedTypeAnalysisContext)
                    continue;

                var path = Path.Combine(asmPath, type.NamespaceAsSubdirs, MiscUtils.CleanPathElement(type.Name + ".cs"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var sb = new StringBuilder();

                //Namespace at top of file
                if (!string.IsNullOrEmpty(type.Namespace))
                    sb.AppendLine($"namespace {type.Namespace};").AppendLine();
                else
                    sb.AppendLine("//Type is in global namespace").AppendLine();

                AppendType(sb, type);

                ret[path] = sb;
            }
        }

        return ret;
    }

    private static void AppendType(StringBuilder sb, TypeAnalysisContext type, int indent = 0)
    {
        // if (type.IsCompilerGeneratedBasedOnCustomAttributes)
        //Do not output compiler-generated types
        // return;

        //Custom attributes for type. Includes a trailing newline
        AppendCustomAttributes(sb, type, indent);

        //Type declaration line
        sb.Append('\t', indent);

        sb.Append(CsFileUtils.GetKeyWordsForType(type));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(type));
        CsFileUtils.AppendInheritanceInfo(type, sb);
        sb.AppendLine();
        sb.Append('\t', indent);
        sb.Append('{');
        sb.AppendLine();

        //Type declaration done, increase indent
        indent++;

        if (type.IsEnumType)
        {
            var enumValues = type.Fields.Where(f => f.IsStatic).ToList();
            enumValues.SortByExtractedKey(e => e.Token); //Not as good as sorting by value but it'll do
            foreach (var enumValue in enumValues)
            {
                sb.Append('\t', indent);
                sb.Append(enumValue.Name);
                sb.Append(" = ");
                sb.Append(InvariantValue(enumValue.BackingData!.DefaultValue));
                sb.Append(',');
                sb.AppendLine();
            }
        }
        else
        {
            //Nested classes, alphabetical order
            var nestedTypes = type.NestedTypes.Clone();
            nestedTypes.SortByExtractedKey(t => t.Name);
            foreach (var nested in nestedTypes)
                AppendType(sb, nested, indent);

            //Fields, offset order, static first
            var fields = type.Fields.Clone();
            fields.SortByExtractedKey(f => f.IsStatic ? f.Offset : f.Offset + 0x1000);
            foreach (var field in fields)
                AppendField(sb, field, indent);

            sb.AppendLine();

            //Events, alphabetical order
            var events = type.Events.Clone();
            events.SortByExtractedKey(e => e.Name);
            foreach (var evt in events)
                AppendEvent(sb, evt, indent);

            //Properties, alphabetical order
            var properties = type.Properties.Clone();
            properties.SortByExtractedKey(p => p.Name);
            foreach (var prop in properties)
                AppendProperty(sb, prop, indent);

            //Methods, alphabetical order
            var methods = type.Methods.Clone();
            methods.SortByExtractedKey(m => m.Name);
            foreach (var method in methods)
                AppendMethod(sb, method, indent);
        }

        //Decrease indent, close brace
        indent--;
        sb.Append('\t', indent);
        sb.Append('}');
        sb.AppendLine().AppendLine();
    }

    private static void AppendField(StringBuilder sb, FieldAnalysisContext field, int indent)
    {
        if (field is InjectedFieldAnalysisContext)
            return;

        //Custom attributes for field. Includes a trailing newline
        AppendCustomAttributes(sb, field, indent);

        //Field declaration line
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForField(field));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(field.FieldType));
        sb.Append(' ');
        sb.Append(field.Name);

        if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
        {
            var fieldRva = field.StaticArrayInitialValue;
            if (fieldRva.Length > 0)
            {
                AppendFieldRvaInitializer(sb, field, fieldRva, indent);
                return;
            }
        }

        if (field.BackingData?.DefaultValue is { } defaultValue)
        {
            sb.Append(" = ");

            if (defaultValue is string stringDefaultValue)
                sb.Append('"').Append(stringDefaultValue).Append('"');
            else if (defaultValue is char charDefaultValue)
                sb.Append("'\\u").Append(((int)charDefaultValue).ToString("X")).Append("'");
            else
                sb.Append(InvariantValue(defaultValue));
        }

        sb.Append("; //Field offset: 0x");
        sb.Append(field.Offset.ToString("X"));

        if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
            sb.Append(" || Has Field RVA (address hidden for diffability)");

        sb.AppendLine();
    }

    private static void AppendFieldRvaInitializer(StringBuilder sb, FieldAnalysisContext field, byte[] data, int indent)
    {
        var tail = $" //Field offset: 0x{field.Offset.ToString("X")} || Has Field RVA (address hidden for diffability)";

        if (TryAscendingInt32Array(data, out var ints))
        {
            sb.Append(" = new int[]").Append(tail).AppendLine();
            sb.Append('\t', indent).Append('{').AppendLine();
            for (var i = 0; i < ints.Length; i += 12)
            {
                var n = Math.Min(12, ints.Length - i);
                sb.Append('\t', indent + 1);
                for (var j = 0; j < n; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append(ints[i + j]);
                }
                if (i + n < ints.Length) sb.Append(',');
                sb.AppendLine();
            }
            sb.Append('\t', indent).Append("};").AppendLine();
            return;
        }

        sb.Append(" = new byte[]").Append(tail).AppendLine();
        sb.Append('\t', indent).Append('{').AppendLine();
        for (var i = 0; i < data.Length; i += 16)
        {
            var n = Math.Min(16, data.Length - i);
            sb.Append('\t', indent + 1);
            for (var j = 0; j < n; j++)
            {
                if (j > 0) sb.Append(", ");
                sb.Append("0x").Append(data[i + j].ToString("X2"));
            }
            if (i + n < data.Length) sb.Append(',');
            sb.AppendLine();
        }
        sb.Append('\t', indent).Append("};").AppendLine();
    }

    //blobs that decode as 0 followed by strictly ascending little-endian int32s are (probably) offset tables,
    //so show them as int[] rather than a hex dump
    private static bool TryAscendingInt32Array(byte[] b, [NotNullWhen(true)] out int[]? ints)
    {
        ints = null;

        const int minElements = 8; //short blobs can pass the ascending check by pure coincidence
        if (b.Length < sizeof(int) * minElements || b.Length % sizeof(int) != 0)
            return false;

        var values = new int[b.Length / sizeof(int)];
        var prev = -1;

        for (var i = 0; i < values.Length; i++)
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i * sizeof(int), sizeof(int)));

            if (i == 0 && v != 0)
                return false;

            if (v <= prev)
                return false;

            prev = v;
            values[i] = v;
        }

        ints = values;
        return true;
    }

    private static void AppendEvent(StringBuilder sb, EventAnalysisContext evt, int indent)
    {
        //Custom attributes for event. Includes a trailing newline
        AppendCustomAttributes(sb, evt, indent);

        //Event declaration line
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForEvent(evt));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(evt.EventType));
        sb.Append(' ');
        sb.Append(evt.Name).AppendLine();
        sb.Append('\t', indent);
        sb.Append('{');
        sb.AppendLine();

        //Add/Remove/Invoke
        indent++;
        if (evt.Adder != null)
            AppendAccessor(sb, evt.Adder, "add", indent, evt.Visibility);
        if (evt.Remover != null)
            AppendAccessor(sb, evt.Remover, "remove", indent, evt.Visibility);
        if (evt.Invoker != null)
            AppendAccessor(sb, evt.Invoker, "fire", indent, evt.Visibility);
        indent--;

        sb.Append('\t', indent);
        sb.Append('}');
        sb.AppendLine().AppendLine();
    }

    private static void AppendProperty(StringBuilder sb, PropertyAnalysisContext prop, int indent)
    {
        //Custom attributes for property. Includes a trailing newline
        AppendCustomAttributes(sb, prop, indent);

        //Property declaration line
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForProperty(prop));
        sb.Append(' ');
        sb.Append(CsFileUtils.GetTypeName(prop.PropertyType));
        sb.Append(' ');
        sb.Append(prop.Name);
        sb.AppendLine();
        sb.Append('\t', indent);
        sb.Append('{');
        sb.AppendLine();

        //Get/Set
        indent++;
        if (prop.Getter != null)
            AppendAccessor(sb, prop.Getter, "get", indent, prop.Visibility);
        if (prop.Setter != null)
            AppendAccessor(sb, prop.Setter, "set", indent, prop.Visibility);
        indent--;

        sb.Append('\t', indent);
        sb.Append('}');
        sb.AppendLine().AppendLine();
    }

    private static void AppendMethod(StringBuilder sb, MethodAnalysisContext method, int indent)
    {
        if (method is InjectedMethodAnalysisContext)
            return;

        //Custom attributes for method. Includes a trailing newline
        AppendCustomAttributes(sb, method, indent);

        //Method declaration line
        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForMethod(method));
        sb.Append(' ');
        if (method.Name is not ".ctor" and not ".cctor")
        {
            sb.Append(CsFileUtils.GetTypeName(method.ReturnType));
            sb.Append(' ');
            sb.Append(method.Name);
        }
        else
        {
            //Constructor
            sb.Append(CsFileUtils.GetTypeName(method.DeclaringType!));
        }

        sb.Append('(');
        sb.Append(CsFileUtils.GetMethodParameterString(method));
        sb.Append(") { }");

        if (IncludeMethodLength)
        {
            sb.Append(" //Length: ");
            sb.Append(method.RawBytes.Length);
        }

        sb.AppendLine().AppendLine();
    }

    //get/set/add/remove/raise
    private static void AppendAccessor(StringBuilder sb, MethodAnalysisContext accessor, string accessorType, int indent, MethodAttributes parentVisibility)
    {
        //Custom attributes for accessor. Includes a trailing newline
        AppendCustomAttributes(sb, accessor, indent);

        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForMethod(accessor, parentVisibility));
        sb.Append(' ');
        sb.Append(accessorType);
        sb.Append(" { } //Length: ");
        sb.Append(accessor.RawBytes.Length);
        sb.AppendLine();
    }

    private static void AppendCustomAttributes(StringBuilder sb, HasCustomAttributes owner, int indent)
        => sb.Append(CsFileUtils.GetCustomAttributeStrings(owner, indent, true, true));

    private static string InvariantValue(object? value)
        => value is null ? "" : value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
}
