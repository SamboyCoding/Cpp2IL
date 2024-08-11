using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.Graphs.Intermediate
{
    internal interface IBlockProcessor
    {
        public void Process(Block block);
    }
}
