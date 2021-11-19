using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Mono.Cecil;

namespace Cpp2IL.Core.Utils
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class TypeDefinitions
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
        
        internal static void BuildPrimitiveMappings()
        {
            Object = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Object")!;
            ValueType = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.ValueType")!;
            String = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.String")!;
            Int64 = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Int64")!;
            Single = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Single")!;
            Double = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Double")!;
            Int32 = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Int32")!;
            UInt32 = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.UInt32")!;
            UInt64 = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.UInt64")!;
            IntPtr = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.IntPtr")!;
            UIntPtr = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.UIntPtr")!;
            Boolean = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Boolean")!;
            Array = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Array")!;
            IEnumerable = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Collections.IEnumerable")!;
            Exception = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Exception")!;
            Void = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Void")!;
            Attribute = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Attribute")!;
            SByte = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.SByte")!;
            Byte = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Byte")!;
            Boolean = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Boolean")!;
            Char = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Char")!;
            Int16 = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Int16")!;
            UInt16 = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.UInt16")!;
            Type = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.Type")!;
            TypedReference = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.TypedReference")!;
            IConvertible = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.IConvertible")!;
            MethodInfo = MiscUtils.TryLookupTypeDefKnownNotGeneric("System.MethodInfo")!;
            

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