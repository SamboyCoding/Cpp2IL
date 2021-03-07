using System;
using System.Collections.Generic;
using System.Linq;
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
        public List<IAnalysedOperand>? Arguments = new List<IAnalysedOperand>();
        private LocalDefinition? _localMade;

        public CallVirtualMethodAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var inReg = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (!(inReg is ConstantDefinition {Value: Il2CppClassIdentifier klass})) return;
            
            var classReadFrom = klass.backingType;
            var slotNum = Utils.GetSlotNum((int) instruction.MemoryDisplacement);
            
            Called = MethodUtils.GetMethodFromVtableSlot(classReadFrom, slotNum);

            if (Called == null) return;

            CalledOn = Called.IsStatic ? null : context.GetLocalInReg("rcx");
            
            if(CalledOn != null)
                RegisterUsedLocal(CalledOn);

            var isVoid = Called.ReturnType.FullName == "System.Void";

            if(!MethodUtils.CheckParameters(instruction, Called, context, !Called.IsStatic, out Arguments, CalledOn?.Type, false))
                AddComment("Arguments are incorrect?");
            
            if(Arguments != null)
                foreach (var analysedOperand in Arguments)
                    if (analysedOperand is LocalDefinition l)
                        RegisterUsedLocal(l);

            if (!isVoid) 
                _localMade = context.MakeLocal(Called.ReturnType, reg: "rax");
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            var argString = $"({string.Join(", ", Arguments?.Select(a => a.GetPseudocodeRepresentation()).ToArray() ?? Array.Empty<string>())})";
            
            if (_localMade != null)
                return $"{_localMade.Type} {_localMade.Name} = {(CalledOn == null ? "" : CalledOn.Name + ".")}{Called?.Name}{argString}";
                    
            return $"{CalledOn?.Name}.{Called?.Name}{argString}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Calls virtual function {Called?.FullName} on instance {CalledOn?.ToString() ?? "null"} with arguments {Arguments?.ToStringEnumerable()}" +
                   (_localMade != null ? $" and stores the result in new local {_localMade} in register rax" : "") +
                   $"\n";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}