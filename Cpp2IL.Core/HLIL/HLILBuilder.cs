using System;
using Cpp2IL.Core.ISIL;

namespace Cpp2IL.Core.HLIL;

public class HLILBuilder
{
    public HLILMnemonic Mnemonic;
    public InstructionSetIndependentOperand[] Operands = Array.Empty<InstructionSetIndependentOperand>();
}