using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
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

        var returnTypeSignature = definition.ReturnTypeContext switch
        {
            { Namespace: nameof(System), Name: "Void" } => "v",
            { IsValueType: true, IsPrimitive: false, Definition: null or { Size: < 0 or > 8 } } => "vi", //Large or Generic Struct returns have a void return type, but the actual return value is the first parameter.
            { IsValueType: true, IsPrimitive: false, Definition.Size: > 4 } => "j", //Medium structs are returned as longs
            { IsValueType: true, IsPrimitive: false, Definition.Size: <= 4 } => "i", //Small structs are returned as ints
            _ => GetSignatureLetter(definition.ReturnTypeContext!)
        };

        return $"{returnTypeSignature}{instanceParam}{string.Join("", definition.Parameters!.Select(p => GetSignatureLetter(p.ParameterTypeContext, p.IsRef)))}i"; //Add an extra i on the end for the method info param
    }

    private static string GetSignatureLetter(TypeAnalysisContext type, bool isRefOrOut = false)
    {
        if (isRefOrOut)
            //ref/out params are passed as pointers 
            return "i";

        if (type is WrappedTypeAnalysisContext)
            //Pointers, arrays, etc are ints
            return "i";

        if (type.IsEnumType)
            type = type.EnumUnderlyingType ?? throw new($"Enum type {type} has no underlying type");

        // var typeDefinition = type.BaseType ?? type.AppContext.SystemTypes.SystemInt32Type;

        return type.Name switch
        {
            "Void" => "v",
            "Int64" => "j",
            "Single" => "f",
            "Double" => "d",
            "Int32" => "i",
            _ when type is { IsValueType: true, IsPrimitive: false, IsEnumType: false, Definition.Size: <= 8 and > 0 } => "j", //TODO check - value types < 16 bytes (including base object header which is irrelevant here) are passed directly as long?
            _ => "i"
        };
    }

    public static string GetGhidraFunctionName(WasmFunctionDefinition functionDefinition)
    {
        var index = functionDefinition.IsImport
            ? ((WasmFile)LibCpp2IlMain.Binary!).FunctionTable.IndexOf(functionDefinition)
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
            return ((WasmFile)LibCpp2IlMain.Binary!).GetFunctionFromIndexAndSignature(context.Definition.MethodPointer, signature);
        }
        catch (Exception e)
        {
            throw new($"Failed to find wasm definition for {context}\nwhich has params {context.Parameters?.ToStringEnumerable()}", e);
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
