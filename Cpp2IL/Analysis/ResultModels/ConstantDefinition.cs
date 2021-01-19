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
            return $"{{'{Name}' (constant value of type {Type.FullName})}}";
        }
    }
}