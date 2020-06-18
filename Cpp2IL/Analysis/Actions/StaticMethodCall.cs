using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions
{
    public class StaticNonVirtMethodCall
    {
        public MethodDefinition Called;
        public List<IAnalysedOperand> Arguments;
    }
}