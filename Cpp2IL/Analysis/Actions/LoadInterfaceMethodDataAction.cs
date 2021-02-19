using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class LoadInterfaceMethodDataAction : BaseAction
    {
        private LocalDefinition _invokedOn;
        private TypeDefinition _interfaceType;
        private int _slotNumber;
        private MethodDefinition resolvedMethod;
        private ConstantDefinition _resultConstant;

        public LoadInterfaceMethodDataAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            if (context.GetConstantInReg("rcx") is {} castConstant
                && castConstant.Value is NewSafeCastResult castResult
                && context.GetConstantInReg("rdx") is {} interfaceConstant
                && interfaceConstant.Value is TypeDefinition interfaceType
                && context.GetConstantInReg("r8") is {} slotConstant
                && slotConstant.Value is int slot
                && context.Actions.FirstOrDefault(a => a is LocateSpecificInterfaceOffsetAction) is LocateSpecificInterfaceOffsetAction locator
            )
            {
                _invokedOn = castResult.original;
                _interfaceType = interfaceType;
                _slotNumber = slot;
                resolvedMethod = SharedState.VirtualMethodsBySlot[(ushort) (locator._matchingInterfaceOffset.offset + _slotNumber)];

                _resultConstant = context.MakeConstant(typeof(MethodDefinition), resolvedMethod, reg: "rax");
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
            return $"Loads the pointer to the interface function defined in {_interfaceType.FullName} - specifically the implementation in {_invokedOn.Type?.FullName} - which has slot number {_slotNumber}, which resolves to {resolvedMethod?.FullName}, and stores in constant {_resultConstant.Name} in rax";
        }
    }
}