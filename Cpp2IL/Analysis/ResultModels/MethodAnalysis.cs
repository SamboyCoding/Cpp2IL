using System.Collections.Generic;
using Cpp2IL.Analysis.Actions;
using Mono.Cecil;

namespace Cpp2IL.Analysis.ResultModels
{
    public class MethodAnalysis
    {
        public List<LocalDefinition> Locals = new List<LocalDefinition>();
        public List<ConstantDefinition> Constants = new List<ConstantDefinition>();
        public List<BaseAction> Actions = new List<BaseAction>();
        private MethodDefinition _forMethod;
        
        internal MethodAnalysis(MethodDefinition forMethod)
        {
            _forMethod = forMethod;
        }
    }
}