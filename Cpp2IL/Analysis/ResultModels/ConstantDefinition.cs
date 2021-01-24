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
                return (string) Value;

            if (Type == typeof(bool))
                return Convert.ToString((bool) Value);
            
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