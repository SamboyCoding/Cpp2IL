using System.Collections.Generic;
using Mono.Cecil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    /// <summary>
    /// Class to replace Cecil's MethodReference for actual analyzed actions, because it has so many shortcomings
    /// </summary>
    public class AnalysedMethodReference
    {
        public TypeReference ReturnType;
        public TypeReference SpecificDeclaringType;
        public MethodDefinition ActualMethodBeingCalled;
        public List<TypeReference> MethodGenericArguments = new();
        public List<AnalyzedMethodParameter> Parameters;

        public class AnalyzedMethodParameter
        {
            public TypeReference Type;
            public string Name;

            public AnalyzedMethodParameter(TypeReference type, string name)
            {
                Type = type;
                Name = name;
            }
        }
    }
}