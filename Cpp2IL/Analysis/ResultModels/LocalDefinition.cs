﻿using System;
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

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((LocalDefinition) obj);
        }

        protected bool Equals(LocalDefinition other)
        {
            return Name == other.Name && Equals(Type, other.Type) && Equals(KnownInitialValue, other.KnownInitialValue) && IsMethodInfoParam == other.IsMethodInfoParam;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Type, KnownInitialValue, IsMethodInfoParam);
        }

        public static bool operator ==(LocalDefinition? left, LocalDefinition? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(LocalDefinition? left, LocalDefinition? right)
        {
            return !Equals(left, right);
        }
    }
}