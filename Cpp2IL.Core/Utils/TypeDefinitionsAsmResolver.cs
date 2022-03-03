using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using AsmResolver.DotNet;

namespace Cpp2IL.Core.Utils
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class TypeDefinitionsAsmResolver
    {
        private static Dictionary<string, TypeDefinition> _primitiveTypeMappings = new();
        
#pragma warning disable 8618
        internal static TypeDefinition Boolean;
        internal static TypeDefinition SByte;
        internal static TypeDefinition Byte;
        internal static TypeDefinition Char;
        internal static TypeDefinition Int16;
        internal static TypeDefinition UInt16;
        internal static TypeDefinition Int32;
        internal static TypeDefinition UInt32;
        internal static TypeDefinition Int64;
        internal static TypeDefinition UInt64;
        internal static TypeDefinition Single;
        internal static TypeDefinition Double;
        internal static TypeDefinition IntPtr;
        internal static TypeDefinition UIntPtr;
        
        internal static TypeDefinition Object;
        internal static TypeDefinition IConvertible;
        internal static TypeDefinition ValueType;
        internal static TypeDefinition Type;
        internal static TypeDefinition TypedReference;
        internal static TypeDefinition String;
        internal static TypeDefinition Array;
        internal static TypeDefinition IEnumerable;
        internal static TypeDefinition Exception;
        internal static TypeDefinition Void;
        internal static TypeDefinition Attribute;
        internal static TypeDefinition MethodInfo;
#pragma warning restore 8618

        internal static void Reset()
        {
            _primitiveTypeMappings.Clear();
        }

        internal static TypeDefinition? GetPrimitive(string name)
        {
            if (_primitiveTypeMappings.TryGetValue(name, out var ret))
                return ret;

            return null;
        }
        
        internal static void CacheNeededTypeDefinitions()
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