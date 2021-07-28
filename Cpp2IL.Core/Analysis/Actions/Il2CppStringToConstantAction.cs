using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class Il2CppStringToConstantAction : BaseAction
    {
        private readonly string _detectedString;
        private string? _destReg;
        private ConstantDefinition? _constantMade;

        //This is specifically for UNMANAGED strings (i.e. those not specified in the metadata, such as names for ICall lookups, etc)
        public Il2CppStringToConstantAction(MethodAnalysis context, Instruction instruction, string detectedString) : base(context, instruction)
        {
            _detectedString = detectedString;

            if (instruction.Mnemonic != Mnemonic.Push)
            {
                _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            }

            _constantMade = context.MakeConstant(typeof(Il2CppString), new Il2CppString(_detectedString, instruction.GetRipBasedInstructionMemoryAddress()), reg: _destReg);
            
            if (instruction.Mnemonic == Mnemonic.Push)
            {
                context.Stack.Push(_constantMade);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads string \"{_detectedString}\" into register {_destReg} as constant {_constantMade}";
        }
    }
}