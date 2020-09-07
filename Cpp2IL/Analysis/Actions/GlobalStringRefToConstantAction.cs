using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalStringRefToConstantAction : BaseAction
    {
        public string? ResolvedString;
        public ConstantDefinition ConstantWritten;
        
        public GlobalStringRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = context.MethodStart + LibCpp2ILUtils.GetOffsetFromMemoryAccess(instruction, instruction.Operands[1]);
            ResolvedString = LibCpp2IlMain.GetLiteralByAddress(globalAddress);

            if (ResolvedString == null) return;
            
            var destReg = instruction.Operands[0].Type == ud_type.UD_OP_REG ? Utils.GetRegisterName(instruction.Operands[0]) : null;
            
            ConstantWritten = context.MakeConstant(typeof(string), ResolvedString, null, destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the string literal \"{ResolvedString}\" as a constant \"{ConstantWritten.Name}\"";
        }
    }
}