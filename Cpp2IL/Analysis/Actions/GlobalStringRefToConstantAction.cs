using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Iced.Intel;
using Mono.Cecil.Cil;
using SharpDisasm.Udis86;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class GlobalStringRefToConstantAction : BaseAction
    {
        public readonly string? ResolvedString;
        public readonly ConstantDefinition? ConstantWritten;
        
        public GlobalStringRefToConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var globalAddress = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            ResolvedString = LibCpp2IlMain.GetLiteralByAddress(globalAddress);

            if (ResolvedString == null) return;
            
            var destReg = instruction.Op0Kind == OpKind.Register ? Utils.GetRegisterNameNew(instruction.Op0Register) : null;
            
            ConstantWritten = context.MakeConstant(typeof(string), ResolvedString, null, destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the string literal \"{ResolvedString}\" as a constant \"{ConstantWritten?.Name}\"";
        }
    }
}