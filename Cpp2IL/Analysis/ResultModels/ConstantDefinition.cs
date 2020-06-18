using System;
using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class ConstantDefinition : IAnalysedOperand
    {
        public string Name;
        public object Value;
        public Type Type;
    }
}