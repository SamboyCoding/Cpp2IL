using System;
using System.Buffers.Binary;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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

    private static Dictionary<string, StringWriter> BuildOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        var ret = new Dictionary<string, StringWriter>();

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

                var stringWriter = new StringWriter();
                var writer = new IndentedTextWriter(stringWriter, "\t");

                //Namespace at top of file
                if (!string.IsNullOrEmpty(type.Namespace))
                {
                    writer.WriteLine($"namespace {type.Namespace};");
                    writer.WriteLineNoTabs(string.Empty);
                }
                else
                {
                    writer.WriteLine("//Type is in global namespace");
                    writer.WriteLineNoTabs(string.Empty);
                }

                WriteType(writer, type);

                ret[path] = stringWriter;
            }
        }

        return ret;
    }

    private static void WriteType(IndentedTextWriter writer, TypeAnalysisContext type)
    {
        // if (type.IsCompilerGeneratedBasedOnCustomAttributes)
        //Do not output compiler-generated types
        // return;

        //Custom attributes for type. Includes a trailing newline
        WriteCustomAttributes(writer, type);

        //Type declaration line
        writer.Write(CsFileUtils.GetKeyWordsForType(type));
        writer.Write(' ');
        writer.Write(CsFileUtils.GetTypeName(type));
        CsFileUtils.WriteInheritanceInfo(type, writer);
        writer.WriteLine();
        writer.WriteLine('{');

        //Type declaration done, increase indent
        writer.Indent++;

        if (type.IsEnumType)
        {
            var enumValues = type.Fields.Where(f => f.IsStatic).ToList();
            enumValues.SortByExtractedKey(e => e.Token); //Not as good as sorting by value but it'll do
            foreach (var enumValue in enumValues)
            {
                writer.Write(enumValue.Name);
                writer.Write(" = ");
                writer.Write(InvariantValue(enumValue.BackingData!.DefaultValue));
                writer.WriteLine(',');
            }
        }
        else
        {
            //Nested classes, alphabetical order
            var nestedTypes = type.NestedTypes.Clone();
            nestedTypes.SortByExtractedKey(t => t.Name);
            foreach (var nested in nestedTypes)
                WriteType(writer, nested);

            //Fields, offset order, static first
            var fields = type.Fields.Clone();
            fields.SortByExtractedKey(f => f.IsStatic ? f.Offset : f.Offset + 0x1000);
            foreach (var field in fields)
                WriteField(writer, field);

            writer.WriteLineNoTabs(string.Empty);

            //Events, alphabetical order
            var events = type.Events.Clone();
            events.SortByExtractedKey(e => e.Name);
            foreach (var evt in events)
                WriteEvent(writer, evt);

            //Properties, alphabetical order
            var properties = type.Properties.Clone();
            properties.SortByExtractedKey(p => p.Name);
            foreach (var prop in properties)
                WriteProperty(writer, prop);

            //Methods, alphabetical order
            var methods = type.Methods.Clone();
            methods.SortByExtractedKey(m => m.Name);
            foreach (var method in methods)
                WriteMethod(writer, method);
        }

        //Decrease indent, close brace
        writer.Indent--;
        writer.WriteLine('}');
        writer.WriteLineNoTabs(string.Empty);
    }

    private static void WriteField(IndentedTextWriter writer, FieldAnalysisContext field)
    {
        if (field is InjectedFieldAnalysisContext)
            return;

        //Custom attributes for field. Includes a trailing newline
        WriteCustomAttributes(writer, field);

        //Field declaration line
        writer.Write(CsFileUtils.GetKeyWordsForField(field));
        writer.Write(' ');
        writer.Write(CsFileUtils.GetTypeName(field.FieldType));
        writer.Write(' ');
        writer.Write(field.Name);

        if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
        {
            var fieldRva = field.StaticArrayInitialValue;
            if (fieldRva.Length > 0)
            {
                WriteFieldRvaInitializer(writer, field, fieldRva);
                return;
            }
        }

        if (field.BackingData?.DefaultValue is { } defaultValue)
        {
            writer.Write(" = ");

            if (defaultValue is string stringDefaultValue)
            {
                writer.Write('"');
                writer.Write(stringDefaultValue);
                writer.Write('"');
            }
            else if (defaultValue is char charDefaultValue)
            {
                writer.Write("'\\u");
                writer.Write(((int)charDefaultValue).ToString("X"));
                writer.Write("'");
            }
            else
                writer.Write(InvariantValue(defaultValue));
        }

        writer.Write("; //Field offset: 0x");
        writer.Write(field.Offset.ToString("X"));

        if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
            writer.Write(" || Has Field RVA (address hidden for diffability)");

        writer.WriteLine();
    }

    private static void WriteFieldRvaInitializer(IndentedTextWriter writer, FieldAnalysisContext field, byte[] data)
    {
        var tail = $" //Field offset: 0x{field.Offset.ToString("X")} || Has Field RVA (address hidden for diffability)";

        if (TryAscendingInt32Array(data, out var ints))
        {
            writer.Write(" = new int[]");
            writer.Write(tail);
            writer.WriteLine();
            writer.WriteLine('{');
            writer.Indent++;
            for (var i = 0; i < ints.Length; i += 12)
            {
                var n = Math.Min(12, ints.Length - i);
                for (var j = 0; j < n; j++)
                {
                    if (j > 0) writer.Write(", ");
                    writer.Write(ints[i + j]);
                }
                if (i + n < ints.Length) writer.Write(',');
                writer.WriteLine();
            }
            writer.Indent--;
            writer.WriteLine("};");
            return;
        }

        writer.Write(" = new byte[]");
        writer.Write(tail);
        writer.WriteLine();
        writer.WriteLine('{');
        writer.Indent++;
        for (var i = 0; i < data.Length; i += 16)
        {
            var n = Math.Min(16, data.Length - i);
            for (var j = 0; j < n; j++)
            {
                if (j > 0) writer.Write(", ");
                writer.Write("0x");
                writer.Write(data[i + j].ToString("X2"));
            }
            if (i + n < data.Length) writer.Write(',');
            writer.WriteLine();
        }
        writer.Indent--;
        writer.WriteLine("};");
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

    private static void WriteEvent(IndentedTextWriter writer, EventAnalysisContext evt)
    {
        //Custom attributes for event. Includes a trailing newline
        WriteCustomAttributes(writer, evt);

        //Event declaration line
        writer.Write(CsFileUtils.GetKeyWordsForEvent(evt));
        writer.Write(' ');
        writer.Write(CsFileUtils.GetTypeName(evt.EventType));
        writer.Write(' ');
        writer.Write(evt.Name);
        writer.WriteLine();
        writer.WriteLine('{');

        //Add/Remove/Invoke
        writer.Indent++;
        if (evt.Adder != null)
            WriteAccessor(writer, evt.Adder, "add", evt.Visibility);
        if (evt.Remover != null)
            WriteAccessor(writer, evt.Remover, "remove", evt.Visibility);
        if (evt.Invoker != null)
            WriteAccessor(writer, evt.Invoker, "fire", evt.Visibility);
        writer.Indent--;

        writer.WriteLine('}');
        writer.WriteLineNoTabs(string.Empty);
    }

    private static void WriteProperty(IndentedTextWriter writer, PropertyAnalysisContext prop)
    {
        //Custom attributes for property. Includes a trailing newline
        WriteCustomAttributes(writer, prop);

        //Property declaration line
        writer.Write(CsFileUtils.GetKeyWordsForProperty(prop));
        writer.Write(' ');
        writer.Write(CsFileUtils.GetTypeName(prop.PropertyType));
        writer.Write(' ');
        writer.Write(prop.Name);
        writer.WriteLine();
        writer.WriteLine('{');

        //Get/Set
        writer.Indent++;
        if (prop.Getter != null)
            WriteAccessor(writer, prop.Getter, "get", prop.Visibility);
        if (prop.Setter != null)
            WriteAccessor(writer, prop.Setter, "set", prop.Visibility);
        writer.Indent--;

        writer.WriteLine('}');
        writer.WriteLineNoTabs(string.Empty);
    }

    private static void WriteMethod(IndentedTextWriter writer, MethodAnalysisContext method)
    {
        if (method is InjectedMethodAnalysisContext)
            return;

        //Custom attributes for method. Includes a trailing newline
        WriteCustomAttributes(writer, method);

        //Method declaration line
        writer.Write(CsFileUtils.GetKeyWordsForMethod(method));
        writer.Write(' ');
        if (method.Name is not ".ctor" and not ".cctor")
        {
            writer.Write(CsFileUtils.GetTypeName(method.ReturnType));
            writer.Write(' ');
            writer.Write(method.Name);
        }
        else
        {
            //Constructor
            writer.Write(CsFileUtils.GetTypeName(method.DeclaringType!));
        }

        writer.Write('(');
        writer.Write(CsFileUtils.GetMethodParameterString(method));
        writer.Write(") { }");

        if (IncludeMethodLength)
        {
            writer.Write(" //Length: ");
            writer.Write(method.RawBytes.Length);
        }

        writer.WriteLine();
        writer.WriteLineNoTabs(string.Empty);
    }

    //get/set/add/remove/raise
    private static void WriteAccessor(IndentedTextWriter writer, MethodAnalysisContext accessor, string accessorType, MethodAttributes parentVisibility)
    {
        //Custom attributes for accessor. Includes a trailing newline
        WriteCustomAttributes(writer, accessor);

        writer.Write(CsFileUtils.GetKeyWordsForMethod(accessor, parentVisibility));
        writer.Write(' ');
        writer.Write(accessorType);
        writer.Write(" { } //Length: ");
        writer.Write(accessor.RawBytes.Length);
        writer.WriteLine();
    }

    private static void WriteCustomAttributes(IndentedTextWriter writer, HasCustomAttributes owner)
        => CsFileUtils.WriteCustomAttributeStrings(owner, writer, true, true);

    private static string InvariantValue(object? value)
        => value is null ? "" : value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
}
