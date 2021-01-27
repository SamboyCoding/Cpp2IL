using System;
using System.Linq;
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


        public int CompareTo(FieldInType other)
        {
            return Offset.CompareTo(other.Offset);
        }

        public FieldDefinition? ResolveToFieldDef()
        {
            var name = Name;
            return DeclaringType.Fields.FirstOrDefault(f => f.Name == name);
        }
    }
}