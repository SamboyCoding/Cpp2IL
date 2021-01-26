using System;
using Mono.Cecil;

namespace Cpp2IL.Analysis
{
    public struct FieldInType : IComparable<FieldInType>
    {
        public string Name;
        public TypeReference? Type;
        public ulong Offset;
        public bool Static;
        public object? Constant;


        public int CompareTo(FieldInType other)
        {
            return Offset.CompareTo(other.Offset);
        }
    }
}