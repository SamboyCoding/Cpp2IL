using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class ComparisonDirectFieldAccess : IComparisonArgument
    {
        public LocalDefinition localAccessedOn;
        public FieldDefinition fieldAccessed;
        public string GetPseudocodeRepresentation()
        {
            return $"{localAccessedOn.Name}.{fieldAccessed.Name}";
        }
        
        public Instruction[] GetILToLoad(MethodAnalysis context, ILProcessor processor)
        {
            var ret = new List<Instruction>();
            
            ret.AddRange(localAccessedOn.GetILToLoad(context, processor));
            ret.Add(processor.Create(OpCodes.Ldfld, fieldAccessed));

            return ret.ToArray();
        }
    }
}