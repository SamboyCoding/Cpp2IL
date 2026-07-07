using System;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class CsFileUtils
{
    /// <summary>
    /// Returns the parameters of the given method as they would likely appear in a C# method signature.
    /// That is to say, joined with a comma and a space, and with each parameter expressed as its type, a space, then its name, and optionally a default value if one is set.
    /// Note this does not include the method name, the return type, or the parentheses around the parameters.
    /// </summary>
    /// <param name="method">The method to generate the parameter string for</param>
    /// <returns>A properly-formatted parameter string as described above.</returns>
    public static string GetMethodParameterString(MethodAnalysisContext method)
    {
        // ToString on the ParameterData will do the right thing.
        return string.Join(", ", method.Parameters);
    }

    /// <summary>
    /// Gets the C# access modifier string for the given type analysis context.
    /// Examples: "public", "internal", "private", "protected", "protected internal", "private protected".
    /// </summary>
    /// <param name="type">The type analysis context to inspect.</param>
    /// <returns>The access modifier keyword appropriate for the type.</returns>
    public static string GetAccessModifiers(TypeAnalysisContext type) => type.Visibility switch
    {
        TypeAttributes.Public => "public",
        TypeAttributes.NotPublic => "internal",
        TypeAttributes.NestedPublic => "public",
        TypeAttributes.NestedAssembly => "internal",
        TypeAttributes.NestedPrivate => "private",
        TypeAttributes.NestedFamily => "protected",
        TypeAttributes.NestedFamORAssem => "protected internal",
        TypeAttributes.NestedFamANDAssem => "private protected",
        _ => throw new ArgumentOutOfRangeException($"Unknown visibility for type {type.FullName}: {type.Visibility}")
    };

    /// <summary>
    /// Gets the C# access modifier string for the given field analysis context.
    /// Examples: "public", "private", "protected", "internal", "protected internal", "private protected".
    /// </summary>
    /// <param name="field">The field analysis context to inspect.</param>
    /// <returns>The access modifier keyword appropriate for the field.</returns>
    public static string GetAccessModifiers(FieldAnalysisContext field) => field.Visibility switch
    {
        FieldAttributes.Public => "public",
        FieldAttributes.Private => "private",
        FieldAttributes.Family => "protected",
        FieldAttributes.Assembly => "internal",
        FieldAttributes.FamORAssem => "protected internal",
        FieldAttributes.FamANDAssem => "private protected",
        _ => throw new ArgumentOutOfRangeException($"Unknown visibility for field {field.DeclaringType.FullName}.{field.Name}: {field.Visibility}")
    };

    /// <summary>
    /// Gets the C# access modifier string for the given method analysis context.
    /// </summary>
    /// <param name="method">The method analysis context to inspect.</param>
    /// <returns>The access modifier keyword appropriate for the method.</returns>
    public static string GetAccessModifiers(MethodAnalysisContext method) => GetAccessModifiers(method.Visibility);

    /// <summary>
    /// Gets the C# access modifier string for the given property analysis context.
    /// </summary>
    /// <param name="property">The property analysis context to inspect.</param>
    /// <returns>The access modifier keyword appropriate for the property.</returns>
    public static string GetAccessModifiers(PropertyAnalysisContext property) => GetAccessModifiers(property.Visibility);

    /// <summary>
    /// Gets the C# access modifier string for the given event analysis context.
    /// </summary>
    /// <param name="evt">The event analysis context to inspect.</param>
    /// <returns>The access modifier keyword appropriate for the event.</returns>
    public static string GetAccessModifiers(EventAnalysisContext evt) => GetAccessModifiers(evt.Visibility);

    private static string GetAccessModifiers(MethodAttributes visibility) => visibility switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Private or MethodAttributes.PrivateScope => "private",
        MethodAttributes.Family => "protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.FamANDAssem => "private protected",
        _ => throw new ArgumentOutOfRangeException($"Unknown visibility: {visibility}")
    };

    /// <summary>
    /// Returns the C# keyword that declares the kind of type represented by the context.
    /// Examples: "class", "struct", "enum", "interface", "delegate".
    /// </summary>
    /// <param name="type">The type analysis context to evaluate.</param>
    /// <returns>The declaration keyword for the type.</returns>
    public static string GetTypeDeclarationKeyword(TypeAnalysisContext type)
    {
        if (type.IsEnumType)
            return "enum";
        if (type.IsValueType)
            return "struct";
        if (type.IsInterface)
            return "interface";
        if (type.IsDelegate)
            return "delegate";
        return "class";
    }

    /// <summary>
    /// Returns a class-level inheritance modifier for the given type, if applicable.
    /// Examples: "static", "abstract", "sealed", or null when no modifier should be emitted.
    /// </summary>
    /// <param name="type">The type analysis context to inspect.</param>
    /// <returns>The inheritance modifier keyword or null.</returns>
    public static string? GetClassInheritanceKeyword(TypeAnalysisContext type)
    {
        if (type.IsStatic)
            return "static";
        if (type.IsAbstract && !type.IsInterface)
            return "abstract";
        if (type.IsSealed && !type.IsValueType && !type.IsDelegate)
            return "sealed";
        return null;
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this type, i.e. access modifiers, static/sealed/etc, and the type of type (class, enum, interface).
    /// Does not include the name of the type.
    /// </summary>
    /// <param name="type">The type to generate the keywords for</param>
    public static string GetKeyWordsForType(TypeAnalysisContext type)
    {
        var visibility = GetAccessModifiers(type);
        var inheritance = GetClassInheritanceKeyword(type);
        var declaration = GetTypeDeclarationKeyword(type);
        return inheritance is null ? $"{visibility} {declaration}" : $"{visibility} {inheritance} {declaration}";
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this field, i.e. access modifiers, static/const/etc.
    /// Does not include the type of the field or its name.
    /// </summary>
    /// <param name="field">The field to generate keywords for</param>
    public static string GetKeyWordsForField(FieldAnalysisContext field)
    {
        var sb = new StringBuilder();
        var attributes = field.Attributes;

        sb.Append(GetAccessModifiers(field)).Append(' ');

        if (attributes.HasFlag(FieldAttributes.Literal))
            sb.Append("const ");
        else
        {
            if (attributes.HasFlag(FieldAttributes.Static))
                sb.Append("static ");

            if (attributes.HasFlag(FieldAttributes.InitOnly))
                sb.Append("readonly ");
        }

        return sb.ToString().TrimEnd();
    }

    private static string? GetVirtualLookupKeyword(bool isInterfaceMember, bool isStatic, bool isAbstract, bool isVirtual, bool isNewSlot, bool isFinal)
    {
        // slot-related modifiers like abstract, virtual, override, sealed

        if (isInterfaceMember)
        {
            if (isAbstract)
                return isStatic ? "abstract" : null;
            else if (isVirtual)
                return isStatic ? "virtual" : null;
            else
                return isStatic ? null : "sealed";
        }
        else if (isAbstract)
        {
            return "abstract";
        }
        else if (isVirtual)
        {
            if (isNewSlot)
                return isFinal ? null : "virtual"; // final, virtual, newslot means an interface implementation
            else
                return isFinal ? "sealed override" : "override";
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    /// Determines the appropriate virtual/slot-related keyword for a method.
    /// Examples: "abstract", "virtual", "override", "sealed override", or null when no slot keyword applies.
    /// </summary>
    /// <param name="method">The method analysis context to inspect.</param>
    /// <returns>The slot-related keyword or null if none should be emitted.</returns>
    public static string? GetVirtualLookupKeyword(MethodAnalysisContext method)
    {
        return GetVirtualLookupKeyword(method.DeclaringType?.IsInterface ?? false, method.IsStatic, method.IsAbstract, method.IsVirtual, method.IsNewSlot, method.IsFinal);
    }

    /// <summary>
    /// Determines the appropriate virtual/slot-related keyword for a property.
    /// </summary>
    /// <param name="property">The property analysis context to inspect.</param>
    /// <returns>The slot-related keyword or null if none should be emitted.</returns>
    public static string? GetVirtualLookupKeyword(PropertyAnalysisContext property)
    {
        return GetVirtualLookupKeyword(property.DeclaringType.IsInterface, property.IsStatic, property.IsAbstract, property.IsVirtual, property.IsNewSlot, property.IsFinal);
    }

    /// <summary>
    /// Determines the appropriate virtual/slot-related keyword for an event.
    /// </summary>
    /// <param name="evt">The event analysis context to inspect.</param>
    /// <returns>The slot-related keyword or null if none should be emitted.</returns>
    public static string? GetVirtualLookupKeyword(EventAnalysisContext evt)
    {
        return GetVirtualLookupKeyword(evt.DeclaringType.IsInterface, evt.IsStatic, evt.IsAbstract, evt.IsVirtual, evt.IsNewSlot, evt.IsFinal);
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this method, i.e. access modifiers, static/abstract/etc.
    /// Does not include the return type, name, or parameters.
    /// </summary>
    /// <param name="method">The method to generate keywords for</param>
    /// <param name="parentVisibility">The visibility of the parent, used to determine if the method's visibility should be included</param>
    public static string GetKeyWordsForMethod(MethodAnalysisContext method, MethodAttributes? parentVisibility = null)
    {
        var sb = new StringBuilder();

        if (method.Visibility != parentVisibility)
            sb.Append(GetAccessModifiers(method)).Append(' ');

        if (parentVisibility is null)
        {
            if (method.IsStatic)
                sb.Append("static ");

            var slotKeyword = GetVirtualLookupKeyword(method);
            if (slotKeyword != null)
                sb.Append(slotKeyword);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this event, i.e. access modifiers, static/abstract/etc.
    /// Does not include the event type or name
    /// </summary>
    /// <param name="evt">The event to generate keywords for</param>
    public static string GetKeyWordsForEvent(EventAnalysisContext evt)
    {
        var sb = new StringBuilder();

        sb.Append(GetAccessModifiers(evt)).Append(' ');

        if (evt.IsStatic)
            sb.Append("static ");

        var slotKeyword = GetVirtualLookupKeyword(evt);
        if (slotKeyword != null)
            sb.Append(slotKeyword).Append(' ');

        sb.Append("event");

        return sb.ToString();
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this property, i.e. access modifiers, static/abstract/etc.
    /// Does not include the property type or name
    /// </summary>
    /// <param name="prop">The property to generate keywords for</param>
    public static string GetKeyWordsForProperty(PropertyAnalysisContext prop)
    {
        var sb = new StringBuilder();

        sb.Append(GetAccessModifiers(prop)).Append(' ');

        if (prop.IsStatic)
            sb.Append("static ");

        var slotKeyword = GetVirtualLookupKeyword(prop);
        if (slotKeyword != null)
            sb.Append(slotKeyword);

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes all the custom attributes for the given entity to the given writer, as they would appear in a C# source file (i.e. properly wrapped in square brackets, with params if known).
    /// Each attribute is written on its own line, indented according to the writer's current <see cref="IndentedTextWriter.Indent"/> level.
    /// </summary>
    /// <param name="context">The entity to write custom attribute strings for</param>
    /// <param name="writer">The writer to write the custom attribute strings to</param>
    /// <param name="analyze">True to call <see cref="HasCustomAttributes.AnalyzeCustomAttributeData"/> before generating.</param>
    /// <param name="includeIncomplete">True to emit custom attributes even if they have required parameters that aren't known</param>
    public static void WriteCustomAttributeStrings(HasCustomAttributes context, IndentedTextWriter writer, bool analyze = true, bool includeIncomplete = true)
    {
        if (analyze)
            context.AnalyzeCustomAttributeData();

        //Sort alphabetically by type name
        context.CustomAttributes!.SortByExtractedKey(a => a.Constructor.DeclaringType!.Name);

        foreach (var analyzedCustomAttribute in context.CustomAttributes!)
        {
            if (!includeIncomplete && !analyzedCustomAttribute.IsSuitableForEmission)
                continue;

            try
            {
                writer.WriteLine(analyzedCustomAttribute.ToString());
            }
            catch (Exception e)
            {
                Logger.WarnNewline("Exception printing/formatting custom attribute: " + e, "C# Generator");
                writer.WriteLine($"/*Cpp2IL: Exception outputting custom attribute of type {analyzedCustomAttribute.Constructor.DeclaringType?.Name ?? "<unknown type?>"}*/");
            }
        }
    }

    /// <summary>
    /// Returns the C#-style name for the given type analysis context.
    /// Handles built-in System type aliases (e.g. System.Int32 -> int), arrays, pointers, by-ref and generic instances.
    /// </summary>
    /// <param name="type">The type analysis context to convert to a C# type name.</param>
    /// <returns>The C# type name as it should appear in source.</returns>
    public static string GetTypeName(TypeAnalysisContext type)
    {
        if (type is WrappedTypeAnalysisContext wrapped)
        {
            var elementTypeName = GetTypeName(wrapped.ElementType);
            switch (wrapped)
            {
                case ArrayTypeAnalysisContext arrayType:
                    {
                        return arrayType.Rank switch
                        {
                            1 => elementTypeName + "[]",
                            2 => elementTypeName + "[,]",
                            3 => elementTypeName + "[,,]",
                            _ => elementTypeName + "[" + new string(',', arrayType.Rank - 1) + "]"
                        };
                    }
                case SzArrayTypeAnalysisContext:
                    return elementTypeName + "[]";
                case PointerTypeAnalysisContext:
                    return elementTypeName + "*";
                case ByRefTypeAnalysisContext:
                    return elementTypeName; //Remove trailing & for ref params
                default:
                    return elementTypeName;
            }
        }

        if (type is GenericInstanceTypeAnalysisContext genericInstanceType)
        {
            var genericTypeName = GetTypeName(genericInstanceType.GenericType);
            var backTickIndex = genericTypeName.LastIndexOf('`');
            return backTickIndex > 0 ? genericTypeName[..backTickIndex] : genericTypeName;
        }

        if (type.Namespace is "System")
        {
            return type.Name switch
            {
                "Void" => "void",
                "Boolean" => "bool",
                "Byte" => "byte",
                "SByte" => "sbyte",
                "Char" => "char",
                "Decimal" => "decimal",
                "Single" => "float",
                "Double" => "double",
                "Int32" => "int",
                "UInt32" => "uint",
                "Int64" => "long",
                "UInt64" => "ulong",
                "Int16" => "short",
                "UInt16" => "ushort",
                "IntPtr" => "nint",
                "UIntPtr" => "nuint",
                "String" => "string",
                "Object" => "object",
                _ => type.Name,
            };
        }
        else
        {
            return type.Name;
        }
    }

    /// <summary>
    /// Writes inheritance data (base class and interfaces) for the given type to the given writer.
    /// If the base class is System.Object, System.ValueType, System.Enum, or System.MulticastDelegate, it will be ignored
    /// </summary>
    /// <param name="type">The type analysis context whose inheritance is to be written.</param>
    /// <param name="writer">The writer to which the inheritance information will be written.</param>
    public static void WriteInheritanceInfo(TypeAnalysisContext type, IndentedTextWriter writer)
    {
        var baseType = type.BaseType;
        var needsBaseClass = baseType is not ReferencedTypeAnalysisContext and ({ Namespace: not "System" } or { Name: not "Object" and not "ValueType" and not "Enum" and not "MulticastDelegate" });
        if (needsBaseClass)
        {
            writer.Write(" : ");
            writer.Write(GetTypeName(baseType!));
        }

        //Interfaces
        if (type.InterfaceContexts.Count <= 0)
            return;

        if (!needsBaseClass)
            writer.Write(" : ");

        var addComma = needsBaseClass;
        foreach (var iface in type.InterfaceContexts)
        {
            if (addComma)
                writer.Write(", ");

            addComma = true;

            writer.Write(GetTypeName(iface));
        }
    }
}
