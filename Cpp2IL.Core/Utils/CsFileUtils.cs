using System;
using System.Collections.Generic;
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
    /// <param name="usingNamespaces"></param>
    /// <returns>A properly-formatted parameter string as described above.</returns>
    public static string GetMethodParameterString(MethodAnalysisContext method, List<string> usingNamespaces=null)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var paramData in method!.Parameters!)
        {
            if (!first)
                sb.Append(", ");

            first = false;
            if (!usingNamespaces.Contains(paramData.ParameterTypeContext.Namespace))
            {
                usingNamespaces.Add(paramData.ParameterTypeContext.Namespace);
            }
            sb.Append(paramData); //ToString on the ParameterData will do the right thing.
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this type, i.e. access modifiers, static/sealed/etc, and the type of type (class, enum, interface).
    /// Does not include the name of the type.
    /// </summary>
    /// <param name="type">The type to generate the keywords for</param>
    public static string GetKeyWordsForType(TypeAnalysisContext type)
    {
        var sb = new StringBuilder();
        var attributes = type.Definition!.Attributes;

        if (attributes.HasFlag(TypeAttributes.NestedPrivate))
            sb.Append("private ");
        else if (attributes.HasFlag(TypeAttributes.Public))
            sb.Append("public ");
        else
            sb.Append("internal "); //private top-level classes don't exist, for obvious reasons

        if (type.IsEnumType)
            sb.Append("enum ");
        else if (type.IsValueType)
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

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this method, i.e. access modifiers, static/const/etc.
    /// Does not include the type of the field or its name.
    /// </summary>
    /// <param name="field">The field to generate keywords for</param>
    public static string GetKeyWordsForField(FieldAnalysisContext field)
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

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this method, i.e. access modifiers, static/abstract/etc.
    /// Does not include the return type, name, or parameters.
    /// </summary>
    /// <param name="method">The method to generate keywords for</param>
    /// <param name="skipSlotRelated">Skip slot-related modifiers like abstract, virtual, override</param>
    /// <param name="skipKeywordsInvalidForAccessors">Skip the public and static keywords, as those aren't valid for property accessors</param>
    public static string GetKeyWordsForMethod(MethodAnalysisContext method, bool skipSlotRelated = false, bool skipKeywordsInvalidForAccessors = false)
    {
        var sb = new StringBuilder();
        var attributes = method.Definition!.Attributes;

        if (!skipKeywordsInvalidForAccessors)
        {
            if (attributes.HasFlag(MethodAttributes.Public))
                sb.Append("public ");
            else if (attributes.HasFlag(MethodAttributes.Family))
                sb.Append("protected ");
        }
        
        if (attributes.HasFlag(MethodAttributes.Assembly))
            sb.Append("internal ");
        else if (attributes.HasFlag(MethodAttributes.Private))
            sb.Append("private ");
        
        if (!skipKeywordsInvalidForAccessors && attributes.HasFlag(MethodAttributes.Static))
            sb.Append("static ");

        if (method.DeclaringType!.Definition!.Attributes.HasFlag(TypeAttributes.Interface) || skipSlotRelated)
        {
            //Deliberate no-op to avoid unnecessarily marking interface methods as abstract
        }
        else if (attributes.HasFlag(MethodAttributes.Abstract))
            sb.Append("abstract ");
        else if (attributes.HasFlag(MethodAttributes.NewSlot))
            sb.Append("override ");
        else if (attributes.HasFlag(MethodAttributes.Virtual))
            sb.Append("virtual ");


        return sb.ToString().Trim();
    }
    
    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this event, i.e. access modifiers, static/abstract/etc.
    /// Does not include the event type or name
    /// </summary>
    /// <param name="evt">The event to generate keywords for</param>
    public static string GetKeyWordsForEvent(EventAnalysisContext evt)
    {
        var sb = new StringBuilder();

        var addAttrs = evt.Adder?.Attributes ?? 0;
        var removeAttrs = evt.Remover?.Attributes ?? 0;
        var raiseAttrs = evt.Invoker?.Attributes ?? 0;

        var all = addAttrs | removeAttrs | raiseAttrs;

        //Accessibility must be that of the most accessible method
        if (addAttrs.HasFlag(MethodAttributes.Public) || removeAttrs.HasFlag(MethodAttributes.Public) || raiseAttrs.HasFlag(MethodAttributes.Public))
            sb.Append("public ");
        else if (all.HasFlag(MethodAttributes.Family)) //Family is only one bit so we can use the OR'd attributes
            sb.Append("protected ");
        if (addAttrs.HasFlag(MethodAttributes.Assembly) || removeAttrs.HasFlag(MethodAttributes.Assembly) || raiseAttrs.HasFlag(MethodAttributes.Assembly))
            sb.Append("internal ");
        else if (all.HasFlag(MethodAttributes.Private))
            sb.Append("private ");
        
        if (all.HasFlag(MethodAttributes.Static))
            sb.Append("static ");

        if (evt.DeclaringType!.Definition!.Attributes.HasFlag(TypeAttributes.Interface))
        {
            //Deliberate no-op to avoid unnecessarily marking interface methods as abstract
        }
        else if (all.HasFlag(MethodAttributes.Abstract))
            sb.Append("abstract ");
        else if (all.HasFlag(MethodAttributes.NewSlot))
            sb.Append("override ");
        else if (all.HasFlag(MethodAttributes.Virtual))
            sb.Append("virtual ");

        sb.Append("event ");

        return sb.ToString().Trim();
    }
    
    /// <summary>
    /// Returns all the keywords that would be present in the c# source file to generate this event, i.e. access modifiers, static/abstract/etc.
    /// Does not include the event type or name
    /// </summary>
    /// <param name="prop">The event to generate keywords for</param>
    public static string GetKeyWordsForProperty(PropertyAnalysisContext prop)
    {
        var sb = new StringBuilder();

        var getterAttributes = prop.Getter?.Attributes ?? 0;
        var setterAttributes = prop.Setter?.Attributes ?? 0;

        var all = getterAttributes | setterAttributes;

        //Accessibility must be that of the most accessible method
        if (getterAttributes.HasFlag(MethodAttributes.Public) || setterAttributes.HasFlag(MethodAttributes.Public))
            sb.Append("public ");
        else if (all.HasFlag(MethodAttributes.Family)) //Family is only one bit so we can use the OR'd attributes
            sb.Append("protected ");
        if (getterAttributes.HasFlag(MethodAttributes.Assembly) || setterAttributes.HasFlag(MethodAttributes.Assembly))
            sb.Append("internal ");
        else if (all.HasFlag(MethodAttributes.Private))
            sb.Append("private ");
        
        if (all.HasFlag(MethodAttributes.Static))
            sb.Append("static ");

        if (prop.DeclaringType!.Definition!.Attributes.HasFlag(TypeAttributes.Interface))
        {
            //Deliberate no-op to avoid unnecessarily marking interface methods as abstract
        }
        else if (all.HasFlag(MethodAttributes.Abstract))
            sb.Append("abstract ");
        else if (all.HasFlag(MethodAttributes.NewSlot))
            sb.Append("override ");
        else if (all.HasFlag(MethodAttributes.Virtual))
            sb.Append("virtual ");

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Returns all the custom attributes for the given entity, as they would appear in a C# source file (i.e. properly wrapped in square brackets, with params if known)
    /// </summary>
    /// <param name="context">The entity to generate custom attribute strings for</param>
    /// <param name="indentCount">The number of tab characters to emit at the start of each line</param>
    /// <param name="analyze">True to call <see cref="HasCustomAttributes.AnalyzeCustomAttributeData"/> before generating.</param>
    /// <param name="includeIncomplete">True to emit custom attributes even if they have required parameters that aren't known</param>
    public static string GetCustomAttributeStrings(HasCustomAttributes context, int indentCount, bool analyze = true, bool includeIncomplete = true)
    {
        var sb = new StringBuilder();

        if(analyze)
            context.AnalyzeCustomAttributeData();

        //Sort alphabetically by type name
        context.CustomAttributes!.SortByExtractedKey(a => a.Constructor.DeclaringType!.Name);
        
        foreach (var analyzedCustomAttribute in context.CustomAttributes!)
        {
            if(!includeIncomplete && !analyzedCustomAttribute.IsSuitableForEmission)
                continue;

            if (indentCount > 0)
                sb.Append('\t', indentCount);

            try
            {
                sb.AppendLine(analyzedCustomAttribute.ToString());
            }
            catch (Exception e)
            {
                Logger.WarnNewline("Exception printing/formatting custom attribute: " + e, "C# Generator");
                sb.Append("/*Cpp2IL: Exception outputting custom attribute of type ").Append(analyzedCustomAttribute.Constructor.DeclaringType?.Name ?? "<unknown type?>").AppendLine("*/");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the name of the given type, as it would appear in a C# source file.
    /// This mainly involves stripping the backtick section from generic type names, and replacing certain system types with their primitive name.
    /// </summary>
    /// <param name="originalName">The original name of the type</param>
    public static string GetTypeName(string originalName)
    {
        if (originalName.Contains("`"))
            //Generics - remove `1 etc
            return originalName.Remove(originalName.IndexOf('`'), 2);

        if (originalName[^1] == '&')
            originalName = originalName[..^1]; //Remove trailing & for ref params

        return originalName switch
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
            "String" => "string",
            "Object" => "object",
            _ => originalName
        };
    }

    /// <summary>
    /// Appends inheritance data (base class and interfaces) for the given type to the given string builder.
    /// If the base class is System.Object, System.ValueType, or System.Enum, it will be ignored
    /// </summary>
    /// <param name="type"></param>
    /// <param name="sb"></param>
    public static void AppendInheritanceInfo(TypeAnalysisContext type, StringBuilder sb)
    {
        var baseType = type.BaseType;
        var needsBaseClass = baseType is { FullName: not "System.Object" and not "System.ValueType" and not "System.Enum" };
        if (needsBaseClass)
            sb.Append(" : ").Append(GetTypeName(baseType!.Name));

        //Interfaces
        if (type.InterfaceContexts.Length <= 0) 
            return;
        
        if (!needsBaseClass)
            sb.Append(" : ");

        var addComma = needsBaseClass;
        foreach (var iface in type.InterfaceContexts)
        {
            if (addComma)
                sb.Append(", ");

            addComma = true;

            sb.Append(GetTypeName(iface.Name));
        }
    }
}
