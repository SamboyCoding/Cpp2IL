using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class ComparisonDirectPropertyAccess : IComparisonArgument
    {
        public LocalDefinition localAccessedOn;
        public PropertyDefinition propertyAccessed;

        public override string ToString()
        {
            return $"{{Property {propertyAccessed} on {localAccessedOn}}}";
        }
        
        public string GetPseudocodeRepresentation()
        {
            return $"{localAccessedOn.Name}.{propertyAccessed.Name}";
        }

        public Instruction[] GetILToLoad(MethodAnalysis context, ILProcessor processor)
        {
            var ret = new List<Instruction>();
            
            ret.AddRange(localAccessedOn.GetILToLoad(context, processor));
            ret.Add(processor.Create(OpCodes.Call, propertyAccessed.GetMethod));

            return ret.ToArray();
        }
    }
}