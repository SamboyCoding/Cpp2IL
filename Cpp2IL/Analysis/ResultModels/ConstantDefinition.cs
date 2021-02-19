using System;
using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class ConstantDefinition : IAnalysedOperand
    {
        public string Name;
        public object Value;
        public Type Type;

        public override string ToString()
        {
            if (Type == typeof(string))
                return $"\"{Value}\"";

            if (Type == typeof(bool))
                return Convert.ToString((bool) Value);

            if (Type == typeof(int) || Type == typeof(ulong))
                return Convert.ToString(Value)!;

            if (Type == typeof(UnknownGlobalAddr))
                return Value.ToString()!;

            if (Type == typeof(MethodDefinition) && Value is MethodDefinition reference)
            {
                return $"{reference.DeclaringType.FullName}.{reference.Name}";
            }

            if (Type == typeof(FieldDefinition) && Value is FieldDefinition fieldDefinition)
            {
                return $"{fieldDefinition.DeclaringType.FullName}.{fieldDefinition.Name}";
            }

            return $"{{'{Name}' (constant value of type {Type.FullName})}}";
        }

        public string GetPseudocodeRepresentation()
        {
            var str = ToString();
            if (str.StartsWith("{"))
                return Name;

            return str;
        }
    }
}