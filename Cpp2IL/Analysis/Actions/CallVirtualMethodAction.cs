using System;
using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class CallVirtualMethodAction : BaseAction
    {
        public LocalDefinition CalledOn;
        public MethodDefinition? Called;
        public List<IAnalysedOperand> Arguments = new List<IAnalysedOperand>();

        public CallVirtualMethodAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var inReg = context.GetOperandInRegister(Utils.GetRegisterName(instruction.Operands[0]));

            if (!(inReg is ConstantDefinition cons) || !(cons.Value is Il2CppClassIdentifier klass)) return;

            var classReadFrom = klass.backingType;
            
            var readOffset = LibCpp2ILUtils.GetOperandMemoryOffset(instruction.Operands[0]);
            Called = Utils.GetMethodFromReadKlassOffset(readOffset);

            if (Called == null) return;

            CalledOn = context.GetLocalInReg("rcx");
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
            return $"Calls virtual function {Called?.FullName} on instance {CalledOn} with {Arguments.Count} arguments\n";
        }
    }
}