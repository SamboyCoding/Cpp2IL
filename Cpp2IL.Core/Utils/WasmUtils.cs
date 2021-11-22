using System.Linq;
using LibCpp2IL;
using LibCpp2IL.Wasm;
using Mono.Cecil;

namespace Cpp2IL.Core.Utils
{
    public static class WasmUtils
    {
        public static string BuildSignature(MethodDefinition definition)
        {
            var instanceParam = definition.IsStatic ? "" : "i";
            return $"{GetSignatureLetter(definition.ReturnType)}{instanceParam}{string.Join("", definition.Parameters.Select(p => p.ParameterType).Select(GetSignatureLetter))}i"; //Add an extra i on the end for the method info param
        }

        private static char GetSignatureLetter(TypeReference typeReference) => GetSignatureLetter(typeReference.Resolve());

        private static char GetSignatureLetter(TypeDefinition typeDefinition)
        {
            if (typeDefinition == TypeDefinitions.Void)
                return 'v';
            if (typeDefinition == TypeDefinitions.Int32)
                return 'i';
            if (typeDefinition == TypeDefinitions.Int64)
                return 'j';
            if (typeDefinition == TypeDefinitions.Single)
                return 'f';
            if (typeDefinition == TypeDefinitions.Double)
                return 'd';

            return 'i'; //Everything else is passed as an int32
        }

        public static string GetGhidraFunctionName(WasmFunctionDefinition functionDefinition)
        {
            var index = ((WasmFile) LibCpp2IlMain.Binary!).FunctionTable.IndexOf(functionDefinition);
            return $"unnamed_function_{index}";
        }
    }
}