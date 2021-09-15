using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class TypeToObjectAction : BaseAction<Instruction>
    {
        private readonly TypeReference? _type;
        private readonly ConstantDefinition? _constant;
        private LocalDefinition? _localMade;

        public TypeToObjectAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _constant = LibCpp2IlMain.Binary!.is32Bit ? context.Stack.Peek() as ConstantDefinition : context.GetConstantInReg("rcx");

            if (!(_constant?.Value is TypeReference type))
                return;

            if (LibCpp2IlMain.Binary.is32Bit)
                context.Stack.Pop(); //Pop off the type global.

            _type = type;

            _localMade = context.MakeLocal(Utils.TryLookupTypeDefKnownNotGeneric("System.Type")!, reg: "rax", knownInitialValue: type);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"System.Type {_localMade?.Name} = typeof({_type})";
        }

        public override string ToTextSummary()
        {
            if (_type == null)
                return $"typeof() call, but couldn't work out what type we wanted - expecting the constant to be in rcx / on top of the stack, but got {_constant?.ToString() ?? "null"}";

            return $"Loads typeof({_type} as a local {_localMade} in rax";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}