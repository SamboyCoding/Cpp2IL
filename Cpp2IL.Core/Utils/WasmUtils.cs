using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.Utils
{
    public static class WasmUtils
    {
        internal static readonly Dictionary<int, List<Il2CppMethodDefinition>> MethodDefinitionIndices = new();
        private static Regex DynCallRemappingRegex = new(@"Module\[\s*[""'](dynCall_[^""']+)[""']\s*\]\s*=\s*Module\[\s*[""']asm[""']\s*\]\[\s*[""']([^""']+)[""']\s*\]\s*\)\.apply", RegexOptions.Compiled);

        public static string BuildSignature(Il2CppMethodDefinition definition)
        {
            var instanceParam = definition.IsStatic ? "" : "i";
            
            //Something still off about p/invoke functions. They do have methodinfo args, but something is wrong somewhere.
            
            return $"{GetSignatureLetter(definition.ReturnType!)}{instanceParam}{string.Join("", definition.Parameters!.Select(p => GetSignatureLetter(p.Type, p.IsRefOrOut)))}i"; //Add an extra i on the end for the method info param
        }

        private static char GetSignatureLetter(Il2CppTypeReflectionData type, bool isRefOrOut = false)
        {
            if(isRefOrOut)
                //ref/out params are passed as pointers 
                return 'i';

            if (type.isPointer)
                //Pointers are ints
                return 'i';

            var typeDefinition = type.baseType ?? LibCpp2IlReflection.GetType("Int32", "System")!;

            return typeDefinition.Name switch
            {
                "Void" => 'v',
                "Int64" => 'j',
                "Single" => 'f',
                "Double" => 'd',
                _ => 'i' //Including Int32
            };
        }

        public static string GetGhidraFunctionName(WasmFunctionDefinition functionDefinition)
        {
            var index = ((WasmFile) LibCpp2IlMain.Binary!).FunctionTable.IndexOf(functionDefinition);
            return $"unnamed_function_{index}";
        }

        public static WasmFunctionDefinition? TryGetWasmDefinition(Il2CppMethodDefinition definition)
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

        public static WasmFunctionDefinition GetWasmDefinition(Il2CppMethodDefinition definition)
        {
            //First, we have to calculate the signature
            var signature = BuildSignature(definition);
            try
            {
                return ((WasmFile) LibCpp2IlMain.Binary!).GetFunctionFromIndexAndSignature(definition.MethodPointer, signature);
            }
            catch (Exception e)
            {
                throw new($"Failed to find wasm definition for {definition}\nwhich has params {definition.Parameters?.ToStringEnumerable()}", e);
            }
        }

        private static void CalculateAllMethodDefinitionIndices()
        {
            foreach (var il2CppMethodDefinition in LibCpp2IlMain.TheMetadata!.methodDefs)
            {
                var methodDefinition = il2CppMethodDefinition;

                try
                {
                    var wasmDef = GetWasmDefinition(methodDefinition);
                    var index = ((WasmFile) LibCpp2IlMain.Binary!).FunctionTable.IndexOf(wasmDef);

                    if (!MethodDefinitionIndices.TryGetValue(index, out var mDefs))
                        MethodDefinitionIndices[index] = mDefs = new();

                    mDefs.Add(methodDefinition);
                }
                catch (Exception)
                {
                    //Ignore
                }
            }
        }

        public static List<Il2CppMethodDefinition>? GetMethodDefinitionsAtIndex(int index)
        {
            if(MethodDefinitionIndices.Count == 0)
                CalculateAllMethodDefinitionIndices();

            if (MethodDefinitionIndices.TryGetValue(index, out var methodDefinitions))
                return methodDefinitions;

            return null;
        }

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
}