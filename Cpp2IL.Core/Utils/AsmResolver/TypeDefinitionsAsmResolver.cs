using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using AsmResolver.DotNet;

namespace Cpp2IL.Core.Utils.AsmResolver
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class TypeDefinitionsAsmResolver
    {
        private static Dictionary<string, TypeDefinition> _primitiveTypeMappings = new();

#pragma warning disable 8618
        public static TypeDefinition Boolean;
        public static TypeDefinition SByte;
        public static TypeDefinition Byte;
        public static TypeDefinition Char;
        public static TypeDefinition Int16;
        public static TypeDefinition UInt16;
        public static TypeDefinition Int32;
        public static TypeDefinition UInt32;
        public static TypeDefinition Int64;
        public static TypeDefinition UInt64;
        public static TypeDefinition Single;
        public static TypeDefinition Double;
        public static TypeDefinition IntPtr;
        public static TypeDefinition UIntPtr;
        
        public static TypeDefinition Object;
        public static TypeDefinition IConvertible;
        public static TypeDefinition ValueType;
        public static TypeDefinition Type;
        public static TypeDefinition TypedReference;
        public static TypeDefinition String;
        public static TypeDefinition Array;
        public static TypeDefinition IEnumerable;
        public static TypeDefinition Exception;
        public static TypeDefinition Void;
        public static TypeDefinition Attribute;
        public static TypeDefinition MethodInfo;
#pragma warning restore 8618

        public static void Reset()
        {
            _primitiveTypeMappings.Clear();
        }

        public static TypeDefinition? GetPrimitive(string name)
        {
            if (_primitiveTypeMappings.TryGetValue(name, out var ret))
                return ret;

            return null;
        }
        
        public static void CacheNeededTypeDefinitions()
        {
            Object = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Object")!;
            ValueType = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.ValueType")!;
            String = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.String")!;
            Int64 = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Int64")!;
            Single = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Single")!;
            Double = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Double")!;
            Int32 = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Int32")!;
            UInt32 = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.UInt32")!;
            UInt64 = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.UInt64")!;
            IntPtr = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.IntPtr")!;
            UIntPtr = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.UIntPtr")!;
            Boolean = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Boolean")!;
            Array = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Array")!;
            IEnumerable = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Collections.IEnumerable")!;
            Exception = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Exception")!;
            Void = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Void")!;
            Attribute = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Attribute")!;
            SByte = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.SByte")!;
            Byte = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Byte")!;
            Boolean = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Boolean")!;
            Char = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Char")!;
            Int16 = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Int16")!;
            UInt16 = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.UInt16")!;
            Type = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.Type")!;
            TypedReference = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.TypedReference")!;
            IConvertible = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.IConvertible")!;
            MethodInfo = AsmResolverUtils.TryLookupTypeDefKnownNotGeneric("System.MethodInfo")!;
            

            _primitiveTypeMappings = new Dictionary<string, TypeDefinition>
            {
                { "string", String },
                { "long", Int64 },
                { "float", Single },
                { "double", Double },
                { "int", Int32 },
                { "bool", Boolean },
                { "uint", UInt32 },
                { "ulong", UInt64 }
            };
        }
    }
}