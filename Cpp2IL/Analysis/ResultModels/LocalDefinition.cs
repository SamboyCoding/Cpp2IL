using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class LocalDefinition : IAnalysedOperand
    {
        public string Name;
        public TypeReference? Type;
        public object? KnownInitialValue;

        //Set during IL generation
        public VariableDefinition? Variable;
        public ParameterDefinition? ParameterDefinition { get; private set; }
        
        public bool IsMethodInfoParam { get; private set; }

        internal LocalDefinition WithParameter(ParameterDefinition parameterDefinition)
        {
            ParameterDefinition = parameterDefinition;
            return this;
        }

        internal LocalDefinition MarkAsIl2CppMethodInfo()
        {
            IsMethodInfoParam = true;
            return this;
        }

        public override string ToString()
        {
            return $"{{'{Name}' (type {Type?.FullName})}}";
        }

        public string GetPseudocodeRepresentation()
        {
            return Name;
        }

        public Instruction[] GetILToLoad(MethodAnalysis context, ILProcessor processor)
        {
            return new[] {context.GetILToLoad(this, processor)};
        }
    }
}