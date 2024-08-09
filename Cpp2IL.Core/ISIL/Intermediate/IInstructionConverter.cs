using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cpp2IL.Core.ISIL.Intermediate
{
    internal interface IInstructionConverter
    {
        public void ConvertIfNeeded(InstructionSetIndependentInstruction instruction);
    }
}
