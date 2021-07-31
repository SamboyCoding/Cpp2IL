using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.ResultModels
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
                return (Convert.ToInt64(Value) != 0).ToString();

            if (Type == typeof(int))
            {
                var intValue = Convert.ToInt64(Value);

                if (intValue > 1024)
                    return $"0x{intValue:X}";

                return Convert.ToString(intValue)!;
            }

            if (Type == typeof(uint) || Type == typeof(ulong))
            {
                var intValue = Convert.ToUInt64(Value);

                if (intValue > 1024)
                    return $"0x{intValue:X}";

                return Convert.ToString(intValue)!;
            }

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

        public Instruction[] GetILToLoad(MethodAnalysis context, ILProcessor ilProcessor)
        {
            if (Type == typeof(string))
                return new[] {ilProcessor.Create(OpCodes.Ldstr, $"{Value}")};

            if (Type == typeof(bool))
                return new[] {ilProcessor.Create(Convert.ToInt64(Value) != 0 ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0)};

            if (Type == typeof(int))
                return new[] {ilProcessor.Create(OpCodes.Ldc_I4, Convert.ToInt32(Value))};

            if (Type == typeof(uint))
                return new[]
                {
                    ilProcessor.Create(OpCodes.Ldc_I4, (int) Convert.ToUInt32(Value)),
                    ilProcessor.Create(OpCodes.Conv_U4) //Convert to uint
                };

            if (Type == typeof(ulong))
                return new[]
                {
                    ilProcessor.Create(OpCodes.Ldc_I4, unchecked((int) (ulong) Value)), //Load as int
                    ilProcessor.Create(OpCodes.Conv_U8) //Convert to ulong
                };

            if (Type == typeof(MethodReference) && Value is MethodReference reference)
                return new[] {ilProcessor.Create(OpCodes.Ldftn, ilProcessor.ImportReference(reference))};
            
            if (Type == typeof(TypeReference) && Value is TypeReference typeReference)
                return new[] {ilProcessor.Create(OpCodes.Ldtoken, ilProcessor.ImportReference(typeReference))};

            if (Type == typeof(FieldDefinition) && Value is FieldDefinition fieldDefinition)
                return new[] {ilProcessor.Create(OpCodes.Ldtoken, ilProcessor.ImportReference(fieldDefinition))};

            throw new TaintedInstructionException($"ConstantDefinition: Don't know how to get IL to load a {Type}");
        }

        
    }
}