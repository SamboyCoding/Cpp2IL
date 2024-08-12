using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Graphs.Processors
{
    internal interface IBlockProcessor
    {
        public void Process(Block block, ApplicationAnalysisContext appContext);
    }
}
