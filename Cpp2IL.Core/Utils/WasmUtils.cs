using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.Utils;

public static class WasmUtils
{
    internal static readonly Dictionary<int, List<Il2CppMethodDefinition>> MethodDefinitionIndices = new();
    private static Regex DynCallRemappingRegex = new(@"Module\[\s*[""'](dynCall_[^""']+)[""']\s*\]\s*=\s*Module\[\s*[""']asm[""']\s*\]\[\s*[""']([^""']+)[""']\s*\]\s*\)\.apply", RegexOptions.Compiled);

    public static string BuildSignature(MethodAnalysisContext definition)
    {
        var instanceParam = definition.IsStatic ? "" : "i";

        //Something still off about p/invoke functions. They do have methodinfo args, but something is wrong somewhere.
        
        //Also, this is STILL wrong for a lot of methods in DateTimeFormat and TimeZoneInfo.
        //It feels like it's something to do with when DateTime is considered a struct and when it's considered a class.
        //But I can find no rhyme nor reason to it.

        var returnTypeSignature = definition.ReturnType.IsWasmPrimitive()
            ? GetSignatureLetter(definition.ReturnType)
            : definition.ReturnType switch
            {
                { Namespace: nameof(System), Name: "Void" } => "v",
                { IsValueType: true, Definition: null or { Size: > 8 } } => "vi", //Large or Generic Struct returns have a void return type, but the actual return value is the first parameter.
                { IsValueType: true, Definition.Size: > 0 and < 4 } => "i", //Small structs are returned as ints
                _ => GetSignatureLetter(definition.ReturnType!, forReturn: true)
            };

        return $"{returnTypeSignature}{instanceParam}{string.Join("", definition.Parameters!.Select(p => GetSignatureLetter(p.ParameterType, p.IsRef)))}i"; //Add an extra i on the end for the method info param
    }

    public static bool IsWasmPrimitive(this TypeAnalysisContext type)
    {
        var typeEnum = type.Type;

        //TODO Validate this, it's only the remnant from some poorly written logic for checking if a TypeAnalysisContext IsPrimitive.
        return typeEnum is >= Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN and <= Il2CppTypeEnum.IL2CPP_TYPE_R8;
    }

    private static string GetSignatureLetter(TypeAnalysisContext type, bool isRefOrOut = false, bool forReturn = false)
    {
        if (isRefOrOut)
            //ref/out params are passed as pointers 
            return "i";

        if (type is WrappedTypeAnalysisContext)
            //Pointers, arrays, etc are ints
            return "i";

        if (type.IsEnumType)
            type = type.EnumUnderlyingType ?? throw new($"Enum type {type} has no underlying type");

        if (type.IsValueType && type is { Namespace: nameof(System), Name: nameof(DateTime) or nameof(TimeSpan) })
            return "j"; //gross hardcoding but i think this is literally the only 2 cases?
        
        if(forReturn && type.IsValueType && type.Fields.Count(f => !f.IsStatic && (f.Attributes & FieldAttributes.Literal) == 0) > 1)
            //this seems to work in my testing, at least
            return "vi";

        // var typeDefinition = type.BaseType ?? type.AppContext.SystemTypes.SystemInt32Type;

        return type.Name switch
        {
            "Void" => "v",
            "Int64" => "j",
            "Single" => "f",
            "Double" => "d",
            "Int32" => "i",
            _ => "i"
        };
    }

    public static string GetGhidraFunctionName(Il2CppBinary binary, WasmFunctionDefinition functionDefinition)
    {
        var index = functionDefinition.IsImport
            ? ((WasmFile)binary).FunctionTable.IndexOf(functionDefinition)
            : functionDefinition.FunctionTableIndex;

        return $"unnamed_function_{index}";
    }

    public static WasmFunctionDefinition? TryGetWasmDefinition(MethodAnalysisContext definition)
    {
        try
        {
            return GetWasmDefinition(definition);
        }
        catch
        {
            return null;
        }
    }

    public static WasmFunctionDefinition GetWasmDefinition(MethodAnalysisContext context)
    {
        if (context.Definition == null)
            throw new($"Attempted to get wasm definition for probably-injected method context: {context}");

        //First, we have to calculate the signature
        var signature = BuildSignature(context);
        try
        {
            return ((WasmFile)context.AppContext.Binary).GetFunctionFromIndexAndSignature(context.Definition.MethodPointer, signature);
        }
        catch (Exception e)
        {
            throw new($"Failed to find wasm definition for {context}\nwhich has params {context.Parameters.ToStringEnumerable()}", e);
        }
    }

    // private static void CalculateAllMethodDefinitionIndices()
    // {
    //     foreach (var il2CppMethodDefinition in LibCpp2IlMain.TheMetadata!.methodDefs)
    //     {
    //         var methodDefinition = il2CppMethodDefinition;
    //
    //         try
    //         {
    //             var wasmDef = GetWasmDefinition(methodDefinition);
    //             var index = ((WasmFile)LibCpp2IlMain.Binary!).FunctionTable.IndexOf(wasmDef);
    //
    //             if (!MethodDefinitionIndices.TryGetValue(index, out var mDefs))
    //                 MethodDefinitionIndices[index] = mDefs = [];
    //
    //             mDefs.Add(methodDefinition);
    //         }
    //         catch (Exception)
    //         {
    //             //Ignore
    //         }
    //     }
    // }
    //
    // public static List<Il2CppMethodDefinition>? GetMethodDefinitionsAtIndex(int index)
    // {
    //     if (MethodDefinitionIndices.Count == 0)
    //         CalculateAllMethodDefinitionIndices();
    //
    //     if (MethodDefinitionIndices.TryGetValue(index, out var methodDefinitions))
    //         return methodDefinitions;
    //
    //     return null;
    // }

    public static Dictionary<string, string> ExtractAndParseDynCallRemaps(string frameworkJsFile)
    {
        //At least one WASM binary found in the wild had the exported function names obfuscated.
        //However, the framework.js file has mappings to the correct names.
        /*e.g.
         var dynCall_viffiiii = Module["dynCall_viffiiii"] = function() {
            return (dynCall_viffiiii = Module["dynCall_viffiiii"] = Module["asm"]["Wo"]).apply(null, arguments)
         }
        */

        var ret = new Dictionary<string, string>();
        var matches = DynCallRemappingRegex.Matches(frameworkJsFile);
        foreach (Match match in matches)
        {
            //Group 1 is the original method name, e.g. dynCall_viffiiii
            //Group 2 is the remapped name, e.g Wo
            var origName = match.Groups[1];
            var remappedName = match.Groups[2];

            ret[remappedName.Value] = origName.Value;
        }

        return ret;
    }
}
