using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Buffers.Binary;
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

        // Field-RVA default bytes (the data IL2CPP hides in global-metadata.dat, e.g. obfuscation "vault" __Raw
        // blobs and Roslyn array initializers) — emit as a REAL C# initializer (`= new byte[]/int[] { .. }`) rather
        // than a trailing comment, so the value reads as code, matching the runtime-init arrays above. Only the
        // bytes are shown; the RVA/pointer address stays hidden, so the file remains diff-stable.
        if ((field.Attributes & FieldAttributes.HasFieldRVA) != 0)
        {
            var fieldRva = field.StaticArrayInitialValue;
            if (fieldRva.Length > 0 )
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
        {
            // Reached only when the field has field RVA but no decodable bytes (StaticArrayInitialValue empty) --
            // the with-bytes case is emitted as a real initializer above and returns before here. Mark it and stop.
            sb.Append(" || Has Field RVA (address hidden for diffability)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine();
    }

    /// <summary>Emit a static array field's runtime-recovered value (from a .cctor RuntimeHelpers.InitializeArray)
    /// as a REAL C# array initializer appended after the field name: <c> = new T[] { .. }; //Field offset ..</c>.
    /// Decoded per the field's declared element type: byte[]/bool[] as hex (16/line); signed/unsigned integer
    /// arrays as decimals (12/line); char[]/float[]/double[]/unknown fall back to a raw byte[] hex dump. The
    /// diffable is a structural view (bodies are empty, not compilable), so an exact-typed literal isn't required
    /// — the point is that the value reads as code, not a comment.</summary>
    private static void AppendRuntimeInitInitializer(StringBuilder sb, FieldAnalysisContext field, byte[] data, int indent)
    {
        var elemName = field.FieldType is SzArrayTypeAnalysisContext sz ? sz.ElementType.FullName : null;
        var (elemSize, signed, keyword) = elemName switch
        {
            "System.SByte" => (1, true, "sbyte"),
            "System.Int16" => (2, true, "short"),
            "System.UInt16" => (2, false, "ushort"),
            "System.Int32" => (4, true, "int"),
            "System.UInt32" => (4, false, "uint"),
            "System.Int64" => (8, true, "long"),
            "System.UInt64" => (8, false, "ulong"),
            _ => (0, false, (string?)null),   // byte/bool/char/float/double/unknown -> raw byte[] hex
        };

        var offset = $" //Field offset: 0x{field.Offset.ToString("X")} (restored from .cctor RuntimeHelpers.InitializeArray)";

        // byte-sized / unknown element -> hex byte[] (16/line); multi-byte integer -> decimals (12/line).
        if (keyword is null || elemSize == 0 || data.Length % elemSize != 0)
        {
            sb.Append(" = new byte[]").Append(offset).AppendLine();
            sb.Append('\t', indent).Append('{').AppendLine();
            for (var i = 0; i < data.Length; i += 16)
            {
                var n = System.Math.Min(16, data.Length - i);
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
            return;
        }

        var values = new List<string>(data.Length / elemSize);
        for (var i = 0; i < data.Length; i += elemSize)
        {
            long v = 0;
            for (var k = 0; k < elemSize; k++) v |= (long)data[i + k] << (8 * k);      // little-endian
            if (signed)
            {
                var bits = elemSize * 8;
                if (bits < 64 && (v & (1L << (bits - 1))) != 0) v -= 1L << bits;         // sign-extend
                values.Add(v.ToString());
            }
            else
            {
                values.Add(((ulong)v & (elemSize == 8 ? ulong.MaxValue : (1UL << (elemSize * 8)) - 1)).ToString());
            }
        }
        sb.Append(" = new ").Append(keyword).Append("[]").Append(offset).AppendLine();
        sb.Append('\t', indent).Append('{').AppendLine();
        for (var i = 0; i < values.Count; i += 12)
        {
            var n = System.Math.Min(12, values.Count - i);
            sb.Append('\t', indent + 1)
              .Append(string.Join(", ", values.GetRange(i, n)))
              .Append(i + n < values.Count ? "," : "")
              .AppendLine();
        }
        sb.Append('\t', indent).Append("};").AppendLine();
    }

    /// <summary>
    /// Render the field-RVA default bytes IL2CPP hides in global-metadata.dat as a REAL C# initializer appended
    /// after the field name: <c> = new byte[] { .. }; //Field offset .. || Has Field RVA</c> — code, not a comment,
    /// matching <see cref="AppendRuntimeInitInitializer"/>. The RVA/pointer address is never emitted, so the file
    /// stays diff-stable. If the whole blob decodes as a little-endian int32 offset table (first == 0, strictly
    /// ascending, non-negative) it is emitted as an <c>int[]</c> literal — that IS the real element type of the
    /// array this data initializes via RuntimeHelpers.InitializeArray; otherwise a <c>byte[]</c> literal.
    /// </summary>
    private static void AppendFieldRvaInitializer(StringBuilder sb, FieldAnalysisContext field, byte[] data, int indent)
    {
        var tail = $" //Field offset: 0x{field.Offset.ToString("X")} || Has Field RVA (address hidden for diffability)";

        if (TryAscendingInt32Array(data, out var ints))
        {
            sb.Append(" = new int[]").Append(tail).AppendLine();
            sb.Append('\t', indent).Append('{').AppendLine();

            for (var i = 0; i < ints.Length; i += 12)
            {
                var n = System.Math.Min(12, ints.Length - i);
                sb.Append('\t', indent + 1)
                  .Append(string.Join(", ", ints.Skip(i).Take(n)))
                  .Append(i + n < ints.Length ? "," : "")
                  .AppendLine();
            }

            sb.Append('\t', indent).Append("};").AppendLine();
            return;
        }

        sb.Append(" = new byte[]").Append(tail).AppendLine();
        sb.Append('\t', indent).Append('{').AppendLine();
        for (var i = 0; i < data.Length; i += 16)
        {
            var n = System.Math.Min(16, data.Length - i);
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

    /// <summary>True (with decoded values) if <paramref name="b"/> is a little-endian int32 offset table: length a
    /// multiple of 4 (&gt;= 2 elements), first element 0, strictly ascending, non-negative. Uniquely matches offset
    /// tables (a <c>static int[]</c>), never a key/blob/char array.</summary>
    private static bool TryAscendingInt32Array(byte[] b, [NotNullWhen(true)] out int[]? ints)
    {
        ints = null;

        if (b.Length < sizeof(int)*2 || b.Length % sizeof(int) != 0)
            return false;

        var values = new List<int>(b.Length / 4);
        long prev = long.MinValue;

        for (var i = 0; i < b.Length; i += 4)
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(i, sizeof(int)));

            if (i == 0 && v != 0)
                return false;

            if (v < 0 || v <= prev)
                return false;

            prev = v;
            values.Add(v);
        }

        ints = values.ToArray();
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
            AppendAccessor(sb, evt.Adder, "add", indent);
        if (evt.Remover != null)
            AppendAccessor(sb, evt.Remover, "remove", indent);
        if (evt.Invoker != null)
            AppendAccessor(sb, evt.Invoker, "fire", indent);
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
            AppendAccessor(sb, prop.Getter, "get", indent);
        if (prop.Setter != null)
            AppendAccessor(sb, prop.Setter, "set", indent);
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
            //Constructor: emit the simple type name (C# ctor syntax), NOT the TypeAnalysisContext whose
            //ToString() is "Type: <FullName>". GetTypeName strips generic-arity backticks.
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
    private static void AppendAccessor(StringBuilder sb, MethodAnalysisContext accessor, string accessorType, int indent)
    {
        //Custom attributes for accessor. Includes a trailing newline
        AppendCustomAttributes(sb, accessor, indent);

        sb.Append('\t', indent);
        sb.Append(CsFileUtils.GetKeyWordsForMethod(accessor, true, true));
        sb.Append(' ');
        sb.Append(accessorType);
        sb.Append(" { } //Length: ");
        sb.Append(accessor.RawBytes.Length);
        sb.AppendLine();
    }

    private static void AppendCustomAttributes(StringBuilder sb, HasCustomAttributes owner, int indent)
        => sb.Append(CsFileUtils.GetCustomAttributeStrings(owner, indent, true, true));

    /// <summary>Format a default/enum constant value culture-invariantly, so float/double literals render as
    /// `0.5` (not `0,5` on a comma-decimal locale, which is invalid C# and breaks diffability across machines).</summary>
    private static string InvariantValue(object? value)
        // null-safe: an enum member / const with no default-value blob (obfuscated metadata) passes null here;
        // the old `StringBuilder.Append(object)` tolerated it, so we must too (else `null.ToString()` -> NRE).
        => value is null ? "" : value is System.IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : value.ToString() ?? "";
}
