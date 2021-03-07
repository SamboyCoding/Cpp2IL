using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallVirtualMethodAction : BaseAction
    {
        public LocalDefinition? CalledOn;
        public MethodDefinition? Called;
        public List<IAnalysedOperand> Arguments = new List<IAnalysedOperand>();

        public CallVirtualMethodAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var inReg = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (!(inReg is ConstantDefinition {Value: Il2CppClassIdentifier klass})) return;
            
            var classReadFrom = klass.backingType;

            var readOffset = instruction.MemoryDisplacement;
            var usage = classReadFrom.VTable[Utils.GetSlotNum((int) readOffset)];
            
            if(usage == null)
                return;

            //TODO These are coming up as null - probably need to check base classes!
            Called = SharedState.UnmanagedToManagedMethods[usage.AsMethod()];

            if (Called == null) return;

            CalledOn = context.GetLocalInReg("rcx");
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"{CalledOn?.Name}.{Called?.Name}() //TODO Arguments and return type";
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