using System;
using Mono.Cecil;

namespace Cpp2IL.Analysis
{
    public struct FieldInType : IComparable<FieldInType>
    {
        public string Name;
        public TypeReference? FieldType;
        public ulong Offset;
        public bool Static;
        public object? Constant;
        public TypeDefinition DeclaringType;
        public FieldDefinition Definition;


        public int CompareTo(FieldInType other)
        {
            return Offset.CompareTo(other.Offset);
        }

        public FieldDefinition? ResolveToFieldDef() => Definition;
    }
}