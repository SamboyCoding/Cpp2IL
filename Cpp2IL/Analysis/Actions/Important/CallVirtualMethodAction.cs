using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallVirtualMethodAction : BaseAction
    {
        public LocalDefinition CalledOn;
        public MethodDefinition? Called;
        public List<IAnalysedOperand> Arguments = new List<IAnalysedOperand>();

        public CallVirtualMethodAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var inReg = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (!(inReg is ConstantDefinition cons) || !(cons.Value is Il2CppClassIdentifier klass)) return;

            var classReadFrom = klass.backingType;

            var readOffset = instruction.MemoryDisplacement;
            Called = Utils.GetMethodFromReadKlassOffset((int) readOffset);

            if (Called == null) return;

            CalledOn = context.GetLocalInReg("rcx");
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
            return $"[!] Calls virtual function {Called?.FullName} on instance {CalledOn} with {Arguments.Count} arguments\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}