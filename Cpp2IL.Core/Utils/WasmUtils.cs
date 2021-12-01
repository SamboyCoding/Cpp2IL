using System;
using System.Collections.Generic;
using System.Linq;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.Utils
{
    public static class WasmUtils
    {
        internal static readonly Dictionary<int, List<Il2CppMethodDefinition>> MethodDefinitionIndices = new();

        public static string BuildSignature(Il2CppMethodDefinition definition)
        {
            var instanceParam = definition.IsStatic ? "" : "i";
            return $"{GetSignatureLetter(definition.ReturnType!)}{instanceParam}{string.Join("", definition.Parameters!.Select(p => p.Type).Select(GetSignatureLetter))}i"; //Add an extra i on the end for the method info param
        }

        private static char GetSignatureLetter(Il2CppTypeReflectionData type)
        {
            var typeDefinition = type.baseType ?? LibCpp2IlReflection.GetType("Int32", "System")!;
            
            if (typeDefinition.Name == "Void")
                return 'v';
            if (typeDefinition.Name == "Int32")
                return 'i';
            if (typeDefinition.Name == "Int64")
                return 'j';
            if (typeDefinition.Name == "Single")
                return 'f';
            if (typeDefinition.Name == "Double")
                return 'd';

            return 'i'; //Everything else is passed as an int32
        }

        public static string GetGhidraFunctionName(WasmFunctionDefinition functionDefinition)
        {
            var index = ((WasmFile) LibCpp2IlMain.Binary!).FunctionTable.IndexOf(functionDefinition);
            return $"unnamed_function_{index}";
        }

        public static WasmFunctionDefinition GetWasmDefinition(Il2CppMethodDefinition definition)
        {
            //First, we have to calculate the signature
            var signature = WasmUtils.BuildSignature(definition);
            return ((WasmFile) LibCpp2IlMain.Binary!).GetFunctionFromIndexAndSignature(definition.MethodPointer, signature);
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
    }
}