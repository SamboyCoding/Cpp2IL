using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class LocalDefinition<T> : IAnalysedOperand<T>
    {
        public string Name;
        public TypeReference? Type;
        public object? KnownInitialValue;

        //Set during IL generation
        public VariableDefinition? Variable;
        public ParameterDefinition? ParameterDefinition { get; private set; }
        
        public bool IsMethodInfoParam { get; private set; }

        internal LocalDefinition<T> WithParameter(ParameterDefinition? parameterDefinition)
        {
            ParameterDefinition = parameterDefinition;
            return this;
        }

        internal LocalDefinition<T> MarkAsIl2CppMethodInfo()
        {
            IsMethodInfoParam = true;
            return this;
        }

        public override string ToString()
        {
            return $"{{'{Name}' ({(ParameterDefinition != null ? "function parameter, " : "")}type {Type?.FullName})}}";
        }

        public string GetPseudocodeRepresentation()
        {
            return Name;
        }

        public Instruction[] GetILToLoad(MethodAnalysis<T> context, ILProcessor processor)
        {
            return new[] {context.GetILToLoad(this, processor)};
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((LocalDefinition<T>) obj);
        }

        protected bool Equals(LocalDefinition<T> other)
        {
            return Name == other.Name && Equals(Type, other.Type) && Equals(KnownInitialValue, other.KnownInitialValue) && IsMethodInfoParam == other.IsMethodInfoParam;
        }

        // public override int GetHashCode()
        // {
        //     return HashCode.Combine(Name, Type, KnownInitialValue, IsMethodInfoParam);
        // }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Name.GetHashCode();
                hashCode = (hashCode * 397) ^ (Type != null ? Type.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (KnownInitialValue != null ? KnownInitialValue.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ IsMethodInfoParam.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(LocalDefinition<T>? left, LocalDefinition<T>? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(LocalDefinition<T>? left, LocalDefinition<T>? right)
        {
            return !Equals(left, right);
        }
    }
}