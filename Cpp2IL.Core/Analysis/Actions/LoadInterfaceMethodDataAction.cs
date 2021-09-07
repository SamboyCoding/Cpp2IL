using System;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class LoadInterfaceMethodDataAction : BaseAction<Instruction>
    {
        private LocalDefinition _invokedOn;
        private TypeDefinition _interfaceType;
        private uint _slotNumber;
        private MethodDefinition? resolvedMethod;
        private ConstantDefinition? _resultConstant;

        public LoadInterfaceMethodDataAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            if (context.GetLocalInReg("rcx") is { } invokedOn
                && context.GetConstantInReg("rdx") is {Value: TypeDefinition interfaceType}
                && context.Actions.LastOrDefault(a => a is LocateSpecificInterfaceOffsetAction) is LocateSpecificInterfaceOffsetAction act)
            {
                _invokedOn = invokedOn;
                _interfaceType = interfaceType;
                // _slotNumber = constantUint;

                if (context.GetConstantInReg("r8") is {Value: uint constantUint})
                {
                    _slotNumber = constantUint;
                } else if (context.GetLocalInReg("r8") is {Type: {Name: "UInt32"}, KnownInitialValue: uint localUint})
                {
                    _slotNumber = localUint;
                }
                else
                {
                    throw new Exception("We had and now don't have a slot number?");
                }
                
                if(act._matchingInterfaceOffset == null)
                    return;

                resolvedMethod = SharedState.VirtualMethodsBySlot[(ushort) (act._matchingInterfaceOffset.offset + _slotNumber)];

                _resultConstant = context.MakeConstant(typeof(MethodDefinition), resolvedMethod, reg: "rax");
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Loads the pointer to the interface function defined in {_interfaceType.FullName} - specifically the implementation in {_invokedOn.Type?.FullName} - which has slot number {_slotNumber}, which resolves to {resolvedMethod?.FullName}, and stores in constant {_resultConstant?.Name} in rax";
        }
    }
}